// File: wwwroot/pages/online-class/classroom-recap.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    if (!Auth.require(['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'], 'online-class.view')) return;
    mountLayout('online-class', t('classroom.dangTai', 'Đang tải...'), '');

    // Khai báo hết hằng và trạng thái TRƯỚC lời gọi nạp dữ liệu ở cuối hàm — một "const" nằm sau một "await" sớm sẽ còn
    // trong Temporal Dead Zone khi phần vẽ chạy.
    const root = document.getElementById('rc-root');
    const roomId = parseInt(qs('id') || '0', 10);
    if (!roomId) { location.href = '/pages/online-class/online-class.html'; return; }

    /** Loại Diễn biến → nhóm lọc, biểu tượng. Nhóm "session" (bắt đầu/kết thúc) chỉ hiện ở "Tất cả". */
    const EVENT_META = {
        Started: ['session', 'ph-play-circle'], Ended: ['session', 'ph-flag-checkered'],
        Joined: ['presence', 'ph-sign-in'], Left: ['presence', 'ph-sign-out'],
        ChatMessage: ['chat', 'ph-chat-circle-text'],
        MicOn: ['media', 'ph-microphone'], MicOff: ['media', 'ph-microphone-slash'],
        CamOn: ['media', 'ph-video-camera'], CamOff: ['media', 'ph-video-camera-slash'],
        RecordingStarted: ['recording', 'ph-record'], RecordingPaused: ['recording', 'ph-pause-circle'],
        RecordingResumed: ['recording', 'ph-play-circle'], RecordingStopped: ['recording', 'ph-stop-circle'],
    };
    let review = null;
    let activePart = 0;
    let activeFilter = 'all';

    root.innerHTML = skeletonHtml('stats', { count: 4 }) + skeletonHtml('block', { height: '380px' });
    try {
        review = await API.get(`/onlineclassroom/${roomId}/review`);
    } catch (ex) {
        // 403: có buổi học nhưng người này chưa từng vào — nói rõ thay vì một trang hỏng.
        document.querySelector('.topbar h1').textContent = t('classroom-recap.xemLaiBuoiHoc', 'Xem lại buổi học');
        root.innerHTML = `<div class="card rc-denied"><div class="card-body">
            ${emptyHtml(ex.status === 403
                ? t('classroom-recap.khongCoQuyen', 'Bạn không có quyền xem buổi học này. Chỉ chủ trì, Admin và những người đã tham gia mới xem lại được.')
                : ex.message, `<i class="ph ${ex.status === 403 ? 'ph-lock-key' : 'ph-warning'}"></i>`)}
            <a class="btn ghost" href="/pages/online-class/online-class.html?tab=history"><i class="ph ph-arrow-left"></i> ${esc(t('classroom-recap.veLichSu', 'Về Lịch sử'))}</a>
        </div></div>`;
        return;
    }

    document.querySelector('.topbar h1').textContent = review.title;
    document.querySelector('.topbar .sub')?.remove();
    document.querySelector('.topbar h1').insertAdjacentHTML('afterend',
        `<div class="sub">${esc(formatDate(review.startedAt, true))} · ${esc(t('classroom-recap.chuTri', 'Chủ trì'))}: ${esc(review.hostUserName)}</div>`);

    render();

    // ==================== VẼ TRANG ====================
    function render() {
        root.innerHTML = `
            <div class="ops">${kpiHtml()}</div>
            <div class="rc-layout">
                <div class="rc-main">${playerHtml()}</div>
                <aside class="rc-aside">${timelineHtml()}</aside>
            </div>`;
        bindPlayer();
        bindTimeline();
        if (review.recordingList.length) selectPart(0);
    }

    function kpiHtml() {
        const totalSize = review.recordingList.reduce((sum, r) => sum + r.fileSizeBytes, 0);
        const cellList = [
            { tone: 'var(--primary)', icon: 'ph-timer', label: t('classroom-recap.thoiLuong', 'Thời lượng buổi học'), value: formatSpanMs(review.durationMs), foot: '' },
            { tone: 'var(--success)', icon: 'ph-users', label: t('classroom-recap.nguoiThamGia', 'Người tham gia'), value: review.participantCount.toLocaleString(Lang.locale), foot: '' },
            { tone: '#8b5cf6', icon: 'ph-chat-circle-text', label: t('classroom-recap.tinNhan', 'Tin nhắn chat'), value: review.chatMessageCount.toLocaleString(Lang.locale), foot: '' },
            { tone: 'var(--danger)', icon: 'ph-video', label: t('classroom-recap.banGhi', 'Bản ghi'), value: review.recordingList.length.toLocaleString(Lang.locale),
              foot: review.recordingList.length ? formatFileSize(totalSize) : t('classroom-recap.khongCoBanGhi', 'Không có bản ghi') },
        ];
        return `<div class="ops-kpi">${cellList.map((c, i) => `
            <div class="ops-kpi-cell" style="--tone:${c.tone};--i:${i}">
                <div class="ops-kpi-top">
                    <span class="ops-kpi-label">${esc(c.label)}</span>
                    <span class="ops-kpi-icon"><i class="ph-fill ${c.icon}"></i></span>
                </div>
                <div class="ops-kpi-value">${esc(c.value)}</div>
                <div class="ops-kpi-foot">${esc(c.foot)}</div>
            </div>`).join('')}</div>`;
    }

    function playerHtml() {
        if (!review.recordingList.length) {
            return `<section class="card"><div class="rc-none"><i class="ph ph-video-camera-slash"></i>
                <b>${esc(t('classroom-recap.buoiHocNayChuaCoBanGhiVideo', 'Buổi học này chưa có bản ghi video.'))}</b>
                <span>${esc(t('classroom-recap.chiCoDienBien', 'Diễn biến của buổi học vẫn xem được ở bên cạnh.'))}</span></div></section>`;
        }
        const partList = review.recordingList.length > 1 ? `<div class="rc-parts" role="group" aria-label="${esc(t('classroom-recap.cacPhan', 'Các bản ghi'))}">${
            review.recordingList.map((r, i) => `<button type="button" class="rc-part" data-part="${i}" aria-pressed="false">
                <i class="ph ph-play-circle"></i>${esc(t('classroom-recap.phanSo', 'Phần {n}', { n: i + 1 }))}
                <small data-part-duration="${i}">${esc(r.durationMs != null ? formatClockMs(r.durationMs) : '')}</small></button>`).join('')}</div>` : '';
        return `<section class="card">
            <div class="rc-stage"><video id="rc-video" controls preload="metadata" playsinline></video></div>
            <div class="rc-player-meta">
                <div><span class="rc-part-title" id="rc-part-title"></span><span class="rc-part-info" id="rc-part-info"></span></div>
                ${review.canDownload ? `<a class="btn ghost sm" id="rc-download" href="#" download><i class="ph ph-download-simple"></i> ${esc(t('classroom-recap.taiXuong', 'Tải xuống'))}</a>` : ''}
            </div>
            ${partList}
        </section>`;
    }

    function timelineHtml() {
        const countOf = (kind) => review.eventList.filter(e => (EVENT_META[e.eventType]?.[0] || 'session') === kind).length;
        const filterList = [
            ['all', t('classroom-recap.locTatCa', 'Tất cả'), review.eventList.length],
            ['chat', t('classroom-recap.locChat', 'Chat'), countOf('chat')],
            ['presence', t('classroom-recap.locRaVao', 'Ra vào'), countOf('presence')],
            ['media', t('classroom-recap.locMicCam', 'Mic & camera'), countOf('media')],
            ['recording', t('classroom-recap.locGhiHinh', 'Ghi hình'), countOf('recording')],
        ];
        return `<section class="card">
            <div class="card-head"><h3>${esc(t('classroom-recap.dienBienBuoiHoc', 'Diễn biến buổi học'))}</h3></div>
            <div class="rc-filters" role="group" aria-label="${esc(t('classroom-recap.locTheoLoai', 'Lọc theo loại'))}">${
                filterList.map(([key, label, count]) => `<button type="button" class="rc-chip" data-filter="${key}" aria-pressed="${key === 'all'}">${esc(label)} <b>${count}</b></button>`).join('')}</div>
            <ol class="rc-timeline" id="rc-timeline">${review.eventList.length
                ? review.eventList.map(eventHtml).join('')
                : `<li class="rc-ev-empty">${esc(t('classroom-recap.chuaCoDienBienNaoDuocGhiLai', 'Chưa có diễn biến nào được ghi lại.'))}</li>`}</ol>
            <div class="rc-ev-empty" id="rc-filter-empty" hidden>${esc(t('classroom-recap.khongCoDienBienLoaiNay', 'Không có diễn biến nào thuộc loại này.'))}</div>
        </section>`;
    }

    function eventHtml(e) {
        const [kind, icon] = EVENT_META[e.eventType] || ['session', 'ph-circle'];
        const name = `<span class="who">${esc(e.userName || '')}</span>`;
        const text = {
            Started: esc(t('classroom-recap.buoiHocBatDau', 'Buổi học bắt đầu')),
            Ended: esc(t('classroom-recap.buoiHocKetThuc', 'Buổi học kết thúc')),
            Joined: t('classroom-recap.evJoined', '{name} đã vào phòng', { name }),
            Left: t('classroom-recap.evLeft', '{name} đã rời phòng', { name }),
            ChatMessage: `${name}<br>${esc(e.message || '')}`,
            MicOn: t('classroom-recap.evMicOn', '{name} bật mic', { name }),
            MicOff: t('classroom-recap.evMicOff', '{name} tắt mic', { name }),
            CamOn: t('classroom-recap.evCamOn', '{name} bật camera', { name }),
            CamOff: t('classroom-recap.evCamOff', '{name} tắt camera', { name }),
            RecordingStarted: esc(t('classroom-recap.ghiHinhBatDau', 'Chủ trì bắt đầu ghi hình')),
            RecordingPaused: esc(t('classroom-recap.ghiHinhTamDung', 'Chủ trì tạm dừng ghi hình')),
            RecordingResumed: esc(t('classroom-recap.ghiHinhTiepTuc', 'Chủ trì tiếp tục ghi hình')),
            RecordingStopped: esc(t('classroom-recap.ghiHinhDung', 'Chủ trì dừng ghi hình')),
        }[e.eventType] || esc(e.eventType);

        const seekable = e.seekRecordingId != null && e.seekOffsetMs != null;
        const videoTime = seekable ? formatClockMs(e.seekOffsetMs) : '';
        const inner = `
            <span class="rc-ev-dot" aria-hidden="true"><i class="ph ${icon}"></i></span>
            <div class="rc-ev-body">
                <div class="rc-ev-text">${text}</div>
                <div class="rc-ev-time">${esc(relativeTime(e.createdAt))} · ${esc(new Date(e.createdAt).toLocaleTimeString(Lang.locale))}${
                    seekable ? `<span class="rc-ev-seek"><i class="ph-fill ph-play"></i> ${esc(videoTime)}</span>`
                             : (review.recordingList.length ? `<span class="rc-ev-nov">${esc(t('classroom-recap.ngoaiVideo', 'ngoài video'))}</span>` : '')}</div>
            </div>`;

        return `<li class="rc-ev${kind === 'chat' ? ' rc-ev-chat' : ''}" data-kind="${kind}" data-event-id="${e.id}">${seekable
            ? `<button type="button" class="rc-ev-hit" data-seek-recording="${e.seekRecordingId}" data-seek-ms="${e.seekOffsetMs}"
                       aria-label="${esc(t('classroom-recap.nhayToi', 'Nhảy tới {time} trong bản ghi', { time: videoTime }))}">${inner}</button>`
            : `<div class="rc-ev-hit" title="${esc(review.recordingList.length ? t('classroom-recap.khongCoTrongVideo', 'Khoảnh khắc này không nằm trong bản ghi nào') : '')}">${inner}</div>`}</li>`;
    }

    // ==================== TƯƠNG TÁC ====================
    function bindPlayer() {
        document.querySelectorAll('.rc-part').forEach(button => button.addEventListener('click', () => selectPart(+button.dataset.part)));
        const video = document.getElementById('rc-video');
        video?.addEventListener('timeupdate', highlightCurrentEvent);
        video?.addEventListener('seeked', highlightCurrentEvent);
        // Bản ghi cũ chưa lưu thời lượng: lấy từ chính video khi nó nạp xong (nếu tệp có thời lượng hữu hạn).
        video?.addEventListener('loadedmetadata', () => {
            const recording = review.recordingList[activePart];
            if (recording.durationMs == null && isFinite(video.duration)) {
                recording.durationMs = video.duration * 1000;
                updatePartInfo();
                const chip = document.querySelector(`[data-part-duration="${activePart}"]`);
                if (chip) chip.textContent = formatClockMs(recording.durationMs);
            }
        });
    }

    function selectPart(index) {
        const recording = review.recordingList[index];
        if (!recording) return;
        activePart = index;
        const video = document.getElementById('rc-video');
        if (video.getAttribute('src') !== recording.fileUrl) video.src = recording.fileUrl;
        document.querySelectorAll('.rc-part').forEach(b => b.setAttribute('aria-pressed', String(+b.dataset.part === index)));
        const download = document.getElementById('rc-download');
        if (download) { download.href = recording.fileUrl; download.setAttribute('download', `${review.title} - ${index + 1}.webm`); }
        updatePartInfo();
    }

    /** Nhảy video tới <paramref name="ms"/> của Bản ghi <paramref name="recordingId"/> rồi phát. Khác phần thì đổi video
     *  trước và chờ nạp xong thông tin (metadata) — đặt vị trí khi video chưa nạp là bị bỏ qua. */
    function seekTo(recordingId, ms) {
        const index = review.recordingList.findIndex(r => r.id === recordingId);
        const video = document.getElementById('rc-video');
        if (index < 0 || !video) return;

        const apply = () => { video.currentTime = ms / 1000; video.play().catch(() => { /* trình duyệt chặn tự phát: để người dùng bấm */ }); };
        if (index === activePart && video.readyState >= 1) apply();
        else {
            video.addEventListener('loadedmetadata', apply, { once: true });
            selectPart(index);
        }
        // Trên màn hình hẹp video nằm trên dòng thời gian: đưa nó vào tầm mắt để thấy ngay kết quả của cú bấm.
        if (window.matchMedia('(max-width: 1000px)').matches)
            video.scrollIntoView({ block: 'nearest', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    }

    /** Làm nổi dòng Diễn biến ứng với vị trí video đang phát: dòng cuối cùng (của phần này) có vị trí <= giờ phát. */
    function highlightCurrentEvent() {
        const video = document.getElementById('rc-video');
        const recording = review.recordingList[activePart];
        if (!video || !recording) return;

        const now = video.currentTime * 1000 + 250; // dung sai nhỏ: tua tới đúng dòng thì dòng đó sáng ngay
        let current = null;
        for (const e of review.eventList) {
            if (e.seekRecordingId === recording.id && e.seekOffsetMs <= now) current = e;
        }
        const timeline = document.getElementById('rc-timeline');
        const previous = timeline.querySelector('.rc-ev.is-now');
        const next = current ? timeline.querySelector(`.rc-ev[data-event-id="${current.id}"]`) : null;
        if (previous === next) return;

        previous?.classList.remove('is-now');
        if (!next) return;
        next.classList.add('is-now');
        // Giữ dòng đang sáng trong khung cuộn của dòng thời gian — chỉ cuộn khung đó, không cuộn cả trang.
        if (next.offsetTop < timeline.scrollTop || next.offsetTop + next.offsetHeight > timeline.scrollTop + timeline.clientHeight)
            timeline.scrollTop = next.offsetTop - timeline.clientHeight / 3;
    }

    function updatePartInfo() {
        const recording = review.recordingList[activePart];
        const total = review.recordingList.length;
        document.getElementById('rc-part-title').textContent = total > 1
            ? t('classroom-recap.phanSoTrenTong', 'Phần {n}/{total}', { n: activePart + 1, total })
            : t('classroom-recap.banGhiBuoiHoc', 'Bản ghi buổi học');
        document.getElementById('rc-part-info').textContent = [
            recording.durationMs != null ? formatClockMs(recording.durationMs) : null,
            formatFileSize(recording.fileSizeBytes),
            `${t('classroom-recap.ghiBoi', 'Ghi bởi')} ${recording.recordedByUserName}`,
        ].filter(Boolean).join(' · ');
    }

    function bindTimeline() {
        document.getElementById('rc-timeline').addEventListener('click', (e) => {
            const row = e.target.closest('[data-seek-ms]');
            if (row) seekTo(+row.dataset.seekRecording, +row.dataset.seekMs);
        });
        document.querySelectorAll('.rc-chip').forEach(chip => chip.addEventListener('click', () => {
            activeFilter = chip.dataset.filter;
            document.querySelectorAll('.rc-chip').forEach(c => c.setAttribute('aria-pressed', String(c === chip)));
            let visible = 0;
            document.querySelectorAll('#rc-timeline .rc-ev').forEach(item => {
                const show = activeFilter === 'all' || item.dataset.kind === activeFilter;
                item.hidden = !show;
                if (show) visible++;
            });
            document.getElementById('rc-filter-empty').hidden = visible > 0 || !review.eventList.length;
        }));
    }

    // ==================== TIỆN ÍCH ====================
    /** Thời gian trôi qua kể từ lúc buổi học bắt đầu: "+12:40" (hoặc "+1:02:03"). Không bao giờ âm. */
    function relativeTime(iso) {
        return `+${formatClockMs(new Date(iso) - new Date(review.startedAt))}`;
    }
})();
