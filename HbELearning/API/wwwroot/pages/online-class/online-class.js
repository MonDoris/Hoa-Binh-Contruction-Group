// File: wwwroot/pages/online-class/online-class.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    if (!Auth.require(['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'], 'online-class.view')) return;
    mountLayout('online-class', t('menu.online-class', 'Phòng học online'), t('online-class.lichCacBuoiDaoTaoTrucTuyen', 'Lịch các buổi đào tạo trực tuyến'));

    // Khai báo hết hằng và trạng thái TRƯỚC lời gọi nạp dữ liệu ở cuối hàm — các hàm vẽ đọc chúng ngay khi dữ liệu về.
    // Mọi vai trò trừ Admin đều tạo được phòng học online — Admin chỉ quản trị hệ thống, không trực tiếp tổ chức buổi học.
    const canManage = !Auth.isAdmin;
    const $ = (id) => document.getElementById(id);
    let roomList = [];
    let tickTimer = null;

    setTopbarAction(
        '<button class="btn ghost" id="btn-join-code"><i class="ph ph-keyhole"></i> <span data-i18n="online-class.nhapMaPhong">Nhập mã phòng</span></button>' +
        (canManage ? '<button class="btn" id="btn-new"><i class="ph ph-plus"></i> <span data-i18n="online-class.taoBuoiHocBtn">Tạo buổi học</span></button>' : ''));
    $('btn-join-code').addEventListener('click', openJoinCodeModal);
    $('btn-join-code-submit').addEventListener('click', submitJoinCode);
    $('otp-boxes').setAttribute('aria-label', t('online-class.maPhong6ChuSo', 'Mã phòng 6 chữ số'));
    setupOtpBoxes();

    if (canManage) $('btn-new').addEventListener('click', () => openForm());
    $('room-form').addEventListener('submit', save);
    $('btn-start-now').addEventListener('click', startNow);

    // Hai thẻ: Buổi học (mặc định) và Lịch sử. Nhớ thẻ đang xem trên địa chỉ trang như các trang khác.
    document.querySelectorAll('#oc-tabs button').forEach(button => button.addEventListener('click', () => {
        showTab(button.dataset.tab, 'list');
        if (button.dataset.tab === 'history') loadHistory();
    }));
    const startTab = tabFromUrl('list');
    showTab(startTab, 'list');
    setupHistoryFilters();
    bindCardActions();

    await load();
    if (startTab === 'history') await loadHistory();

    // ==================== DANH SÁCH BUỔI HỌC ====================
    async function load() {
        ['oc-live-grid', 'oc-upcoming-grid'].forEach(id => { $(id).innerHTML = skeletonHtml('cards', { count: 2 }); });
        $('oc-upcoming-section').hidden = false;
        try {
            roomList = await API.get('/onlineclassroom');
            renderRooms();
        } catch (ex) {
            $('oc-live-section').hidden = true;
            $('oc-upcoming-grid').innerHTML = `<div style="grid-column:1/-1">${emptyHtml(ex.message, '<i class="ph ph-warning"></i>')}</div>`;
        }
    }

    /** Chia danh sách theo trạng thái. Buổi đã kết thúc không nằm ở đây — chúng thuộc tab Lịch sử. */
    function renderRooms() {
        const live = roomList.filter(r => r.status === 'Live');
        const upcoming = roomList.filter(r => r.status === 'Scheduled').sort((a, b) => new Date(a.scheduledStart) - new Date(b.scheduledStart));
        const cancelled = roomList.filter(r => r.status === 'Cancelled');

        $('oc-live-section').hidden = !live.length;
        $('oc-live-count').textContent = live.length || '';
        $('oc-live-grid').innerHTML = live.map(roomCardHtml).join('');

        $('oc-upcoming-count').textContent = upcoming.length || '';
        $('oc-upcoming-grid').innerHTML = upcoming.length ? upcoming.map(roomCardHtml).join('')
            : `<div class="card oc-empty" style="grid-column:1/-1">${emptyHtml(
                t(live.length ? 'online-class.chuaCoBuoiSapToi' : 'online-class.chuaCoBuoiHocOnlineNao', live.length ? 'Chưa có buổi học nào được lên lịch.' : 'Chưa có buổi học online nào.'),
                '<i class="ph ph-video-camera"></i>')}
                ${canManage ? `<div style="text-align:center;margin-top:12px"><button type="button" class="btn sm" data-new-room><i class="ph ph-plus"></i> ${esc(t('online-class.taoBuoiHoc', '+ Tạo buổi học').replace(/^\+\s*/, ''))}</button></div>` : ''}</div>`;

        $('oc-cancelled-section').hidden = !cancelled.length;
        $('oc-cancelled-count').textContent = cancelled.length || '';
        $('oc-cancelled-grid').innerHTML = cancelled.map(roomCardHtml).join('');

        tick();
        clearInterval(tickTimer);
        tickTimer = setInterval(tick, 15000);
    }

    function roomCardHtml(r) {
        const isHost = r.hostUserId === Auth.profile?.id;
        const isLive = r.status === 'Live', isScheduled = r.status === 'Scheduled';
        const canEdit = isScheduled && (Auth.isAdmin || isHost);
        // Chỉ chủ trì/Admin vào thẳng được không cần mã — người khác dù thấy buổi học trong danh sách này cũng phải có mã
        // phòng (hoặc liên kết mời kèm mã) do chủ trì chia sẻ mới vào được (ClassRoomHub.JoinRoom).
        const canJoinDirect = Auth.isAdmin || isHost;
        const tone = isLive ? 'oc-live' : isScheduled ? 'oc-soon' : 'oc-off';
        const stateText = isLive ? t('online-class.dangDienRa', 'Đang diễn ra') : isScheduled ? t('online-class.daLenLich', 'Đã lên lịch') : t('online-class.daHuy', 'Đã hủy');

        // r.joinCode chỉ khác null với chủ trì/Admin (backend không lộ mã cho người khác) — hiện kèm nút sao chép liên kết mời.
        const codeHtml = r.joinCode && (isLive || isScheduled)
            ? `<div class="oc-code"><b aria-label="${esc(t('classroom.maPhong', 'Mã phòng'))} ${esc(r.joinCode)}">${esc(r.joinCode)}</b>
                <button type="button" class="btn ghost sm icon" data-copy-link="${r.id}" data-code="${esc(r.joinCode)}"
                        title="${esc(t('classroom.saoChepLienKetMoi', 'Sao chép liên kết mời'))}" aria-label="${esc(t('classroom.saoChepLienKetMoi', 'Sao chép liên kết mời'))}"><i class="ph ph-copy"></i></button></div>`
            : '';

        const actions = [];
        if (isLive && canJoinDirect) actions.push(`<a class="btn sm" href="/pages/online-class/classroom.html?id=${r.id}"><i class="ph ph-sign-in"></i> ${esc(t('online-class.vaoPhong', 'Vào phòng'))}</a>`);
        else if (isLive) actions.push(`<button type="button" class="btn sm" data-enter-code><i class="ph ph-keyhole"></i> ${esc(t('online-class.nhapMaDeVao', 'Nhập mã để vào'))}</button>`);
        if (isScheduled && isHost) actions.push(`<a class="btn sm" href="/pages/online-class/classroom.html?id=${r.id}"><i class="ph ph-play"></i> ${esc(t('my-course.batDau', 'Bắt đầu'))}</a>`);
        if (isScheduled && !isHost) actions.push(`<span class="oc-wait"><i class="ph ph-hourglass-medium"></i> ${esc(t('online-class.choChuTriBatDau', 'Chờ chủ trì bắt đầu'))}</span>`);
        if (canEdit) actions.push(`<button type="button" class="btn ghost sm" data-edit="${r.id}"><i class="ph ph-pencil-simple"></i> ${esc(t('common.edit', 'Sửa'))}</button>`);
        if (canEdit) actions.push(`<button type="button" class="btn ghost danger sm" data-cancel="${r.id}"><i class="ph ph-prohibit"></i> ${esc(t('common.cancel', 'Hủy'))}</button>`);
        if (Auth.isAdmin && !isLive) actions.push(`<button type="button" class="btn ghost danger sm" data-del="${r.id}"><i class="ph ph-trash"></i> ${esc(t('common.delete', 'Xóa'))}</button>`);

        const timeCell = isLive && r.actualStartAt ? `data-elapsed="${esc(r.actualStartAt)}"` : isScheduled ? `data-countdown="${esc(r.scheduledStart)}"` : '';
        return `<article class="oc-card ${tone}">
            <div class="oc-top">
                <span class="oc-state"><i class="oc-dot"></i>${esc(stateText)}</span>
                <span class="oc-count" ${timeCell}></span>
            </div>
            <h4>${esc(r.title)}</h4>
            <div class="oc-meta"><i class="ph ph-calendar-blank"></i><span>${esc(formatDate(r.scheduledStart, true))}${r.scheduledEnd ? ' — ' + esc(formatDate(r.scheduledEnd, true)) : ''}</span></div>
            <div class="oc-meta"><i class="ph ph-user-circle"></i><span>${esc(r.hostUserName)}${isHost ? ` <span class="badge blue">${esc(t('online-class.histYouHost', 'Bạn chủ trì'))}</span>` : ''}</span></div>
            ${codeHtml}
            ${actions.length ? `<div class="oc-actions">${actions.join('')}</div>` : ''}
        </article>`;
    }

    /** Đếm ngược tới giờ bắt đầu (buổi sắp diễn ra) và thời gian đã diễn ra (buổi đang diễn ra). Cập nhật mỗi 15 giây. */
    function tick() {
        if (document.hidden) return;
        const now = Date.now();
        document.querySelectorAll('[data-countdown]').forEach(el => {
            el.textContent = countdownText(new Date(el.dataset.countdown).getTime() - now);
        });
        document.querySelectorAll('[data-elapsed]').forEach(el => {
            el.textContent = t('online-class.daDienRa', 'Đã diễn ra {time}', { time: formatSpanMs(now - new Date(el.dataset.elapsed).getTime()) });
        });
    }

    function countdownText(ms) {
        if (ms <= 0) return t('online-class.daQuaGio', 'Đã quá giờ bắt đầu {time}', { time: formatSpanMs(-ms) });
        const minutes = Math.ceil(ms / 60000);
        if (minutes <= 1) return t('online-class.sapBatDau', 'Sắp bắt đầu');
        if (minutes < 60) return t('online-class.conPhut', 'Còn {m} phút', { m: minutes });
        const hours = Math.floor(minutes / 60);
        if (hours < 24) return t('online-class.conGio', 'Còn {h} giờ {m} phút', { h: hours, m: minutes % 60 });
        return t('online-class.conNgay', 'Còn {d} ngày {h} giờ', { d: Math.floor(hours / 24), h: hours % 24 });
    }

    /** Một chỗ nghe cho mọi nút trên thẻ (ủy quyền sự kiện) — thẻ được dựng lại bằng innerHTML mỗi lần tải. */
    function bindCardActions() {
        const onClick = async (e) => {
            const button = e.target.closest('button');
            if (!button) return;

            if (button.dataset.edit) openForm(roomList.find(x => x.id == button.dataset.edit));
            else if (button.hasAttribute('data-new-room')) openForm();
            else if (button.hasAttribute('data-enter-code')) openJoinCodeModal();
            else if (button.dataset.cancel) {
                if (!await confirmDialog(t('online-class.huyBuoiHocNay', 'Hủy buổi học này?'), { danger: true, okText: t('online-class.huyBuoiHoc', 'Hủy buổi học') })) return;
                try { const r = await API.post(`/onlineclassroom/${button.dataset.cancel}/cancel`); toastOk(r.message); load(); }
                catch (ex) { toastErr(ex.message); }
            } else if (button.dataset.del) {
                if (!await confirmDialog(t('online-class.xoaBuoiHocNay', 'Xóa buổi học này?'), { danger: true, okText: t('common.delete', 'Xóa') })) return;
                try { const r = await API.del(`/onlineclassroom/${button.dataset.del}`); toastOk(r.message); load(); loadHistoryIfShown(); }
                catch (ex) { toastErr(ex.message); }
            } else if (button.dataset.copyLink) {
                const link = `${location.origin}/pages/online-class/classroom.html?id=${button.dataset.copyLink}&code=${encodeURIComponent(button.dataset.code)}`;
                try { await navigator.clipboard.writeText(link); toastOk(t('classroom.daSaoChepLienKetMoiVaoPhong', 'Đã sao chép liên kết mời vào phòng.')); }
                catch { toastErr(t('classroom.khongSaoChepDuocTrinhDuyetChan', 'Không sao chép được — trình duyệt chặn quyền truy cập clipboard.')); }
            }
        };
        $('tab-list').addEventListener('click', onClick);
        $('tab-history').addEventListener('click', onClick);
    }

    function loadHistoryIfShown() {
        if ($('tab-history').classList.contains('active')) loadHistory();
    }

    // ==================== LỊCH SỬ ====================
    // Các Buổi học ĐÃ KẾT THÚC mà người xem được phép xem (Chủ trì, Admin, người từng vào). Quyền do server quyết
    // định; trang chỉ hiển thị những gì server trả về.
    function setupHistoryFilters() {
        let timer = null;
        const reload = () => loadHistory();
        $('hist-keyword').addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(reload, 300); });
        ['hist-from', 'hist-to', 'hist-mine'].forEach(id => $(id).addEventListener('change', reload));
        $('hist-reset').addEventListener('click', () => {
            $('hist-keyword').value = '';
            $('hist-from').value = '';
            $('hist-to').value = '';
            $('hist-mine').checked = false;
            reload();
        });
    }

    function historyFiltered() {
        return !!($('hist-keyword').value.trim() || $('hist-from').value || $('hist-to').value || $('hist-mine').checked);
    }

    async function loadHistory() {
        const grid = $('hist-grid');
        const count = $('hist-count');
        const params = new URLSearchParams();
        const keyword = $('hist-keyword').value.trim();
        if (keyword) params.set('keyword', keyword);
        if ($('hist-from').value) params.set('from', $('hist-from').value);
        if ($('hist-to').value) params.set('to', $('hist-to').value);
        if ($('hist-mine').checked) params.set('onlyMine', 'true');

        // Đổi bộ lọc thì giữ nguyên danh sách cũ ở độ mờ thấp thay vì chớp khung xương — không nhảy bố cục.
        if (grid.children.length) grid.style.opacity = '.5'; else grid.innerHTML = skeletonHtml('cards', { count: 3 });
        try {
            const list = await API.get(`/onlineclassroom/history${params.size ? `?${params}` : ''}`);
            count.textContent = list.length ? t('online-class.histCount', '{count} buổi học', { count: list.length }) : '';
            grid.innerHTML = list.length ? list.map(historyCardHtml).join('')
                : `<div style="grid-column:1/-1">${emptyHtml(historyFiltered()
                    ? t('online-class.histNoMatch', 'Không có buổi học nào khớp bộ lọc.')
                    : t('online-class.histEmpty', 'Chưa có buổi học nào trong Lịch sử.'), '<i class="ph ph-clock-counter-clockwise"></i>')}</div>`;
        } catch (ex) {
            count.textContent = '';
            grid.innerHTML = `<div style="grid-column:1/-1">${emptyHtml(ex.message, '<i class="ph ph-warning"></i>')}</div>`;
        } finally {
            grid.style.opacity = '';
        }
    }

    function historyCardHtml(r) {
        const recordingText = r.recordingCount
            ? `${t('online-class.histRecordings', '{count} bản ghi', { count: r.recordingCount })} · ${formatFileSize(r.recordingSizeBytes)}`
            : t('online-class.histNoRecording', 'Không có bản ghi');
        return `<article class="oc-card hist-card">
            <div class="oc-top">
                <span class="oc-state"><i class="oc-dot"></i>${esc(t('online-class.daKetThuc', 'Đã kết thúc'))}</span>
                ${r.isHost ? `<span class="badge blue nowrap">${esc(t('online-class.histYouHost', 'Bạn chủ trì'))}</span>` : ''}
            </div>
            <h4>${esc(r.title)}</h4>
            <div class="oc-meta"><i class="ph ph-calendar-blank"></i><span>${esc(formatDate(r.startedAt, true))}</span></div>
            <div class="oc-meta"><i class="ph ph-user-circle"></i><span>${esc(r.hostUserName)}</span></div>
            <div class="hist-stats">
                <span title="${esc(t('online-class.histDuration', 'Thời lượng'))}"><i class="ph ph-timer"></i>${esc(formatSpanMs(r.durationMs))}</span>
                <span><i class="ph ph-users"></i>${esc(t('online-class.histPeople', '{count} người', { count: r.participantCount }))}</span>
                <span><i class="ph ph-video"></i>${esc(recordingText)}</span>
            </div>
            <div class="oc-actions" style="border-top:0;padding-top:2px">
                <a class="btn ghost sm" href="/pages/online-class/classroom-recap.html?id=${r.id}"><i class="ph ph-play-circle"></i> ${esc(t('online-class.xemLai', 'Xem lại'))}</a>
                ${Auth.isAdmin ? `<button type="button" class="btn ghost danger sm" data-del="${r.id}"><i class="ph ph-trash"></i> ${esc(t('common.delete', 'Xóa'))}</button>` : ''}
            </div>
        </article>`;
    }

    // ==================== TẠO / SỬA BUỔI HỌC ====================
    function openForm(room) {
        $('modal-title').textContent = room ? t('online-class.suaBuoiHoc', 'Sửa buổi học') : t('online-class.taoBuoiHocOnline', 'Tạo buổi học online');
        $('f-id').value = room?.id || '';
        $('f-title').value = room?.title || '';
        $('f-start').value = room?.scheduledStart ? toLocalInput(room.scheduledStart) : '';
        $('f-end').value = room?.scheduledEnd ? toLocalInput(room.scheduledEnd) : '';
        openModal('modal-room');
    }

    /** ISO -> "yyyy-MM-ddTHH:mm" cho input datetime-local (theo giờ địa phương). */
    function toLocalInput(iso) {
        const d = new Date(iso);
        const pad = (n) => String(n).padStart(2, '0');
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    }

    async function save(e) {
        e.preventDefault();
        const id = $('f-id').value;
        const end = $('f-end').value;

        const body = {
            title: $('f-title').value.trim(),
            scheduledStart: $('f-start').value,
            scheduledEnd: end || null
        };

        try {
            if (id) await API.put(`/onlineclassroom/${id}`, body);
            else await API.post('/onlineclassroom', body);
            toastOk(t('online-class.daLuuBuoiHoc', 'Đã lưu buổi học.'));
            closeModal('modal-room');
            load();
        } catch (ex) { toastErr(ex.message); }
    }

    /** Tạo/lưu buổi học với thời gian bắt đầu = ngay bây giờ rồi vào thẳng phòng học với vai trò chủ trì. */
    async function startNow() {
        const titleInput = $('f-title');
        if (!titleInput.value.trim()) { toastErr(t('online-class.vuiLongNhapTieuDe', 'Vui lòng nhập tiêu đề.')); titleInput.focus(); return; }

        $('f-start').value = toLocalInput(new Date().toISOString());

        const id = $('f-id').value;
        const body = {
            title: titleInput.value.trim(),
            scheduledStart: $('f-start').value,
            scheduledEnd: $('f-end').value || null
        };

        try {
            let roomId = id;
            if (id) await API.put(`/onlineclassroom/${id}`, body);
            else roomId = (await API.post('/onlineclassroom', body)).id;

            closeModal('modal-room');
            location.href = `/pages/online-class/classroom.html?id=${roomId}`;
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== NHẬP MÃ PHÒNG ====================
    /** Vào phòng bằng mã tham gia (do chủ trì chia sẻ riêng) — không cần buổi học đó nằm trong danh sách này.
     * Mã phòng luôn đúng 6 ký tự (xem GenerateUniqueJoinCodeAsync ở backend) nên dùng 6 ô nhập kiểu OTP,
     * gõ tới đâu tự nhảy ô kế tiếp tới đó, gõ xong ô cuối tự vào phòng luôn cho nhanh. */
    function openJoinCodeModal() {
        const boxes = document.querySelectorAll('#otp-boxes .otp-box');
        boxes.forEach(b => { b.value = ''; b.classList.remove('filled'); });
        showOtpError('');
        openModal('modal-join-code');
        boxes[0]?.focus();
    }

    function showOtpError(message) {
        $('otp-boxes').classList.toggle('is-error', !!message);
        $('otp-error').innerHTML = message ? `<i class="ph ph-warning-circle"></i> ${esc(message)}` : '';
    }

    function setupOtpBoxes() {
        const boxes = [...document.querySelectorAll('#otp-boxes .otp-box')];
        boxes.forEach((box, i) => {
            box.addEventListener('input', () => {
                showOtpError('');
                box.value = box.value.replace(/[^0-9]/g, '').slice(-1);
                box.classList.toggle('filled', !!box.value);
                if (box.value && i < boxes.length - 1) boxes[i + 1].focus();
                else if (box.value && i === boxes.length - 1) submitJoinCode();
            });
            box.addEventListener('keydown', (e) => {
                if (e.key === 'Backspace' && !box.value && i > 0) {
                    boxes[i - 1].focus();
                    boxes[i - 1].value = '';
                    boxes[i - 1].classList.remove('filled');
                } else if (e.key === 'ArrowLeft' && i > 0) boxes[i - 1].focus();
                else if (e.key === 'ArrowRight' && i < boxes.length - 1) boxes[i + 1].focus();
                else if (e.key === 'Enter') submitJoinCode();
            });
            box.addEventListener('paste', (e) => {
                e.preventDefault();
                const text = (e.clipboardData || window.clipboardData).getData('text').replace(/[^0-9]/g, '');
                if (!text) return;
                showOtpError('');
                [...text].slice(0, boxes.length).forEach((ch, idx) => { boxes[idx].value = ch; boxes[idx].classList.add('filled'); });
                const filledCount = Math.min(text.length, boxes.length);
                if (filledCount >= boxes.length) submitJoinCode();
                else boxes[filledCount].focus();
            });
        });
    }

    function submitJoinCode() {
        const boxes = [...document.querySelectorAll('#otp-boxes .otp-box')];
        const code = boxes.map(b => b.value).join('');
        if (code.length < boxes.length) {
            showOtpError(t('online-class.vuiLongNhapDu6KyTuMaPhong', 'Vui lòng nhập đủ 6 ký tự mã phòng.'));
            return;
        }
        closeModal('modal-join-code');
        location.href = `/pages/online-class/classroom.html?code=${encodeURIComponent(code)}`;
    }
})();
