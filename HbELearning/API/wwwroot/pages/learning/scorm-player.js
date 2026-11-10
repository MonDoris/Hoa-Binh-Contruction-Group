// File: wwwroot/pages/learning/scorm-player.js
(async function () {
    await Lang.ready;
    if (!Auth.require()) return;

    // Ba cách mở: học viên mở KHÓA HỌC của lượt học (enrollmentId) — khóa học chính là một gói SCORM 2004 4th
    // Edition; người soạn / người duyệt xem thử khóa học (courseId) hoặc xem thử riêng một gói nhập ngoài (packageId).
    const enrollmentId = parseInt(qs('enrollmentId') || '0', 10) || null;
    const courseId = parseInt(qs('courseId') || '0', 10) || null;
    const packageId = parseInt(qs('packageId') || '0', 10) || null;
    // Mở thẳng một mục: activityId (L5, Q3, P2...), hoặc lessonId / quizId từ các đường dẫn cũ.
    const targetActivityId = qs('activityId')
        || (qs('lessonId') ? `L${parseInt(qs('lessonId'), 10)}` : null)
        || (qs('quizId') ? `Q${parseInt(qs('quizId'), 10)}` : null)
        || (qs('scenarioSceneId') ? `S${parseInt(qs('scenarioSceneId'), 10)}` : null);
    const backUrl = enrollmentId ? '/pages/my-course/my-course.html'
        : courseId ? `/pages/course/course-detail.html?id=${courseId}` : '/pages/course/course.html';
    const backLabel = enrollmentId ? t('scorm.khoaHocCuaToi', 'Khóa học của tôi') : t('scorm.quayLai', 'Quay lại');
    if (!enrollmentId && !courseId && !packageId) { location.href = backUrl; return; }

    // ---------- Trạng thái — khai báo TRƯỚC lần await đầu tiên (xem ghi chú TDZ ở learn.html) ----------
    let session = null;        // ScormSessionDto gần nhất
    let delivery = null;       // hoạt động đang phát
    let rte = null;            // bộ API của SCO đang phát (ScormRte.Scorm2004Api / Scorm12Api)
    let pendingNav = null;     // yêu cầu điều hướng của LMS đang chờ SCO kết thúc
    let forceTimer = null;
    let commitTimer = null, queuedCommit = null, commitChain = Promise.resolve();
    let autoCommitTimer = null;
    let navigating = false;
    let stopped = false;       // phiên bị cửa sổ khác chiếm — ngừng mọi thao tác
    let leaving = false;
    let forcedTermination = false;
    let reviewOffered = false; // đã mời đánh giá trong lần mở trang này
    let exitAfterSave = false; // bấm Thoát: lưu chỗ đang học xong thì về trang trước

    const shell = document.getElementById('sp-shell');
    const frame = document.getElementById('sp-frame');
    const overlay = document.getElementById('sp-overlay');
    const tocBox = document.getElementById('sp-toc');

    mountLayout(enrollmentId ? 'my-course' : 'course', t('classroom.dangTai', 'Đang tải...'), '');
    setTopbarAction(`<a href="${backUrl}" class="btn ghost sm" id="sp-back"><i class="ph ph-arrow-left"></i> ${esc(backLabel)}</a>`);
    showOverlay(`<div class="sp-panel">${skeletonHtml('detail', { lines: 4 })}</div>`);

    try {
        if (window.matchMedia('(max-width: 900px)').matches) shell.classList.add('toc-hidden');
    } catch { /* matchMedia không có — giữ mục lục mở */ }

    bindToolbar();
    window.addEventListener('pagehide', onPageHide);
    await launch(false);

    // ==================== VÒNG ĐỜI PHIÊN ====================

    async function launch(restart, target = targetActivityId) {
        try {
            const dto = await API.post('/scorm/launch', {
                enrollmentId,
                // Xem thử riêng một gói nhập ngoài thì gửi packageId; còn lại là mở cả khóa học.
                courseId: enrollmentId || packageId ? null : courseId,
                packageId: enrollmentId ? null : packageId,
                targetActivityId: target || null,
                restart,
            });
            applySession(dto);
        } catch (ex) {
            showOverlay(panelHtml('ph-warning', t('scorm.khongMoDuocKhoaHoc', 'Không mở được nội dung'), ex.message,
                `<a class="btn" href="${backUrl}">${esc(backLabel)}</a>`));
        }
    }

    /** Nhận một phiên mới từ máy chủ: vẽ lại mục lục / nút, rồi phát hoạt động được giao (nếu có). */
    function applySession(dto) {
        // Bấm Thoát: chỗ đang học đã được lưu xong, về trang trước. leaving = true để onPageHide không gửi thêm lệnh.
        if (exitAfterSave) { leaving = true; location.href = backUrl; return; }
        session = dto;
        document.querySelector('.topbar h1').textContent = dto.packageTitle;
        document.title = `${dto.packageTitle} · HB E-Learning`;
        renderToc();
        renderButtons();

        if (dto.delivery) {
            deliver(dto.delivery);
        } else {
            unloadContent();
            delivery = null;
            document.getElementById('sp-activity').textContent = dto.packageTitle;
            showOverlay(dto.endSession ? endPanelHtml(dto) : messagePanelHtml(dto));
            bindOverlay();
            if (dto.endSession) offerCourseReview(dto);
        }
        renderSub();
    }

    /** Vừa hoàn thành khóa học → mở ngay hộp đánh giá, nhưng chỉ khi học viên chưa đánh giá và mỗi lần mở trang một lần. */
    async function offerCourseReview(dto) {
        if (reviewOffered || !enrollmentId || dto.isPreview || !dto.isCourse || !dto.summary.completedAt) return;
        reviewOffered = true;
        try {
            const feedback = await API.getSilent(`/feedback/course/${dto.courseId}`);
            if (!feedback.canReview || feedback.itemList.some(x => x.kind === 'Review' && x.isMine)) return;
        } catch { return; }
        document.getElementById('feedback-title').textContent =
            `${t('feedback.tieuDe', 'Phản hồi khóa học')} — ${dto.courseTitle}`;
        openModal('modal-feedback');
        mountCourseFeedback({ container: document.getElementById('feedback-body'), courseId: dto.courseId });
    }

    function deliver(item) {
        delivery = item;
        stopAutoCommit();
        // Trạng thái đọc được từ DOM — để người hỗ trợ (và kiểm thử tự động) biết SCO đã bắt tay với LMS chưa.
        shell.dataset.activity = item.activityId;
        shell.dataset.scoPhase = 'idle';
        forcedTermination = false;
        document.getElementById('sp-activity').textContent = item.title;

        // Mỗi SCO một bộ API mới: SCO sau không được thấy trạng thái "đã kết thúc" của SCO trước. Phiên bản bộ API là
        // của TỪNG hoạt động — khóa học 2004 có thể nhúng gói 1.2 (SCO của gói đó tìm window.API).
        const saved = item.cmiJson ? safeParse(item.cmiJson) : null;
        const version = item.runtimeVersion || session.version;
        const factory = version === '1.2' ? ScormRte.Scorm12Api : ScormRte.Scorm2004Api;
        rte = factory({
            init: item.init,
            saved,
            navValid,
            onInitialize: () => { shell.dataset.scoPhase = 'running'; startAutoCommit(); },
            onCommit: payload => scheduleCommit(payload),
            onTerminate: (payload, navRequest) => onScoTerminate(payload, navRequest),
            // SCO gọi API sau khi phiên của nó đã kết thúc — chuẩn trả lỗi 1xx cho SCO; ghi lại để người hỗ trợ lần ra.
            onLateCall: (name, element) => console.warn(`[SCORM] ${item.activityId}: ${name}(${element}) sau khi đã kết thúc (${forcedTermination ? 'LMS đã ép kết thúc' : 'SCO đã gọi Terminate'})`),
        });
        // Đặt lên window ĐÚNG tên chuẩn quy định — đây là chỗ SCO dò tìm (đi ngược lên cửa sổ cha). Bộ API của
        // phiên bản kia gỡ đi, để gói 1.2 không vớ nhầm bộ 2004 và ngược lại.
        // Lưu ý: hằng `API` (lớp gọi máy chủ trong js/api.js) là khai báo cấp tập lệnh, KHÔNG phải thuộc tính của
        // window — gán window.API ở đây không đè lên nó.
        if (version === '1.2') { window.API = rte.api; delete window.API_1484_11; }
        else { window.API_1484_11 = rte.api; delete window.API; }

        hideOverlay();
        frame.src = item.launchUrl;
        renderButtons();
    }

    function unloadContent() {
        stopAutoCommit();
        if (frame.getAttribute('src') !== 'about:blank') frame.src = 'about:blank';
    }

    // ==================== GIAO TIẾP VỚI SCO ====================

    /** adl.nav.request_valid.* — trả lời từ bản thử trước của máy chủ ở lần đồng bộ gần nhất. */
    function navValid(kind, target) {
        const nav = session?.nav;
        if (!nav) return 'unknown';
        if (kind === 'continue') return String(!!nav.canContinue);
        if (kind === 'previous') return String(!!nav.canPrevious);
        if (kind === 'choice') return String(nav.choiceValidIdList.includes(target));
        if (kind === 'jump') return String(nav.jumpValidIdList.includes(target));
        return 'unknown';
    }

    /** SCO gọi Commit: gom các lần gọi dồn dập (SCO 1.2 hay Commit sau MỖI lần ghi) thành một lượt gửi. */
    function scheduleCommit(payload) {
        if (stopped) return;
        queuedCommit = payload;
        clearTimeout(commitTimer);
        commitTimer = setTimeout(flushCommit, 800);
        setSaveState('saving');
    }

    function flushCommit() {
        clearTimeout(commitTimer);
        const payload = queuedCommit;
        queuedCommit = null;
        if (!payload || !delivery || stopped) return commitChain;
        const activityId = delivery.activityId;
        commitChain = commitChain.then(async () => {
            try {
                const result = await API.postSilent(`/scorm/registration/${session.registrationId}/commit`, {
                    sessionId: session.sessionId, activityId, runtime: payload,
                });
                session.nav = result.nav;
                session.summary = result.summary;
                renderToc();
                renderButtons();
                renderSub();
                setSaveState('saved');
                // Bài cuối vừa nộp làm cả khóa hoàn thành → mời đánh giá ngay, không đợi học viên bấm Kết thúc.
                offerCourseReview(session);
            } catch (ex) {
                handleRequestError(ex);
            }
        });
        return commitChain;
    }

    /** LMS tự lưu mỗi phút một lần trong lúc SCO đang chạy — gói không bao giờ gọi Commit thì cũng không mất
     *  quá một phút tiến độ khi máy tắt đột ngột. */
    function startAutoCommit() {
        stopAutoCommit();
        autoCommitTimer = setInterval(() => {
            if (rte && rte.phase === 'running' && !stopped) scheduleCommit(rte.buildPayload());
        }, 60000);
    }
    function stopAutoCommit() { clearInterval(autoCommitTimer); autoCommitTimer = null; }

    /** SCO gọi Terminate (hoặc LMS ép kết thúc). Có thể đang ở giữa sự kiện unload của iframe — gửi ngay, giữ
     *  kết nối sống (keepalive) để yêu cầu không bị trình duyệt hủy giữa đường. */
    function onScoTerminate(payload, scoNavRequest) {
        shell.dataset.scoPhase = 'terminated';
        clearTimeout(forceTimer);
        clearTimeout(commitTimer);
        queuedCommit = null;
        stopAutoCommit();

        // Điều hướng do LMS khởi xướng (bấm Tiếp, chọn mục lục, đóng trang) thắng yêu cầu của SCO.
        const request = pendingNav || parseNavRequest(scoNavRequest);
        pendingNav = null;
        sendNavigate({ activityId: delivery?.activityId, runtime: payload, request: request?.request, target: request?.target });
    }

    function parseNavRequest(value) {
        if (!value) return null;
        const match = /^\{target=([^}]+)\}(choice|jump)$/.exec(value);
        if (match) return { request: match[2], target: match[1] };
        return { request: value, target: null };
    }

    /** Yêu cầu điều hướng từ giao diện LMS. SCO còn đang chạy thì phải để nó kết thúc trước: gỡ nội dung khỏi
     *  khung (SCO tự gọi Terminate trong unload), quá 2 giây mà không thấy thì LMS tự kết thúc thay. */
    function requestNavigation(request, target = null) {
        if (navigating || stopped || !session) return;
        if (rte && rte.phase === 'running') {
            pendingNav = { request, target };
            navigating = true;
            renderButtons();
            showOverlay(`<div class="sp-panel">${skeletonHtml('detail', { lines: 3 })}</div>`);
            // Hẹn giờ phải đặt TRƯỚC khi gỡ nội dung, và chỉ nhắm đúng bộ API của SCO đang chạy lúc này: trình duyệt
            // gọi beforeunload của iframe NGAY TRONG lệnh đổi src, SCO thường Terminate xong ở đó và SCO kế tiếp đã
            // được giao trước khi hẹn giờ chạy — kiểm "rte" ở thời điểm hẹn giờ là ép kết thúc nhầm SCO mới.
            const runningRte = rte;
            clearTimeout(forceTimer);
            forceTimer = setTimeout(() => {
                if (rte === runningRte && runningRte.phase === 'running') {
                    forcedTermination = true;
                    const payload = runningRte.forceTerminate();
                    onScoTerminate(payload, null);
                }
            }, 2000);
            frame.src = 'about:blank';
            return;
        }
        unloadContent();
        sendNavigate({
            // SCO được giao mà chưa từng Initialize (asset, hoặc gói hỏng): báo hoạt động đó để máy chủ kết thúc nó.
            activityId: rte && rte.phase === 'idle' ? delivery?.activityId : null,
            runtime: null, request, target,
        });
    }

    async function sendNavigate({ activityId, runtime, request, target }) {
        if (stopped) return;
        navigating = true;
        renderButtons();
        const body = { sessionId: session.sessionId, activityId: activityId || null, runtime: runtime || null, request: request || null, targetActivityId: target || null };
        try {
            await commitChain;
            const dto = await postKeepalive(`/scorm/registration/${session.registrationId}/navigate`, body);
            if (leaving) return;
            navigating = false;
            rte = null;
            applySession(dto);
        } catch (ex) {
            navigating = false;
            exitAfterSave = false;
            if (leaving) return;
            handleRequestError(ex);
            renderButtons();
        }
    }

    /** fetch giữ kết nối sống qua lúc trang đóng (keepalive) — trình duyệt giới hạn thân 64 KB cho loại này, gói có
     *  suspend_data lớn thì gửi thường (vẫn tới nơi khi trang còn mở, đủ cho mọi trường hợp trừ đóng tab). */
    async function postKeepalive(path, body) {
        const text = JSON.stringify(body);
        const response = await fetch(`/api${path}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${Auth.token}` },
            body: text,
            keepalive: text.length < 60000,
        });
        const data = await response.json().catch(() => null);
        if (!response.ok) {
            const error = new Error(data?.message || data?.title || `Lỗi ${response.status}`);
            error.status = response.status;
            throw error;
        }
        return data;
    }

    function handleRequestError(ex) {
        if (ex.status === 409) {
            stopped = true;
            unloadContent();
            showOverlay(panelHtml('ph-browsers', t('scorm.phienDaMoNoiKhac', 'Đang được mở ở cửa sổ khác'), ex.message,
                `<a class="btn" href="${location.href}">${esc(t('scorm.moLaiOCuaSoNay', 'Mở lại ở cửa sổ này'))}</a>`));
            return;
        }
        setSaveState('error', ex.message);
        toastErr(ex.message);
    }

    /** Đóng / rời trang: SCO đang chạy thì đặt sẵn "Lưu và thoát" để lệnh Terminate trong unload của nó mang theo;
     *  nội dung đã kết thúc thì tự gửi lệnh tạm dừng. Dữ liệu cuối cùng do chính SCO gửi trong unload. */
    function onPageHide() {
        if (leaving || stopped || !session) return;
        leaving = true;
        if (rte && rte.phase === 'running') {
            pendingNav = { request: 'suspendAll', target: null };
            return;
        }
        if (session.nav?.hasCurrent) {
            const body = JSON.stringify({ sessionId: session.sessionId, request: 'suspendAll' });
            fetch(`/api/scorm/registration/${session.registrationId}/navigate`, {
                method: 'POST', keepalive: true, body,
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${Auth.token}` },
            }).catch(() => {});
        }
    }

    // ==================== GIAO DIỆN ====================

    function bindToolbar() {
        document.getElementById('sp-toc-toggle').addEventListener('click', () => shell.classList.toggle('toc-hidden'));
        document.getElementById('sp-prev').addEventListener('click', () => requestNavigation('previous'));
        document.getElementById('sp-next').addEventListener('click', () => requestNavigation('continue'));
        // Một nút Thoát duy nhất: lưu chỗ đang học (suspendAll) rồi về trang trước. Việc về trang nằm ở applySession
        // khi máy chủ đã xác nhận lưu xong — rời trang ngay thì lệnh lưu có thể bị hủy giữa đường.
        document.getElementById('sp-exit').addEventListener('click', () => {
            if (!session?.nav?.hasCurrent) { leaving = true; location.href = backUrl; return; }
            exitAfterSave = true;
            requestNavigation('suspendAll');
        });
        document.getElementById('sp-fullscreen').addEventListener('click', () => {
            if (document.fullscreenElement) document.exitFullscreen?.();
            else shell.requestFullscreen?.().catch(() => {});
        });
        tocBox.addEventListener('click', (e) => {
            const node = e.target.closest('[data-choice]');
            if (!node || node.disabled) return;
            if (window.matchMedia?.('(max-width: 900px)').matches) shell.classList.add('toc-hidden');
            requestNavigation('choice', node.dataset.choice);
        });
    }

    function renderButtons() {
        const nav = session?.nav;
        const hide = new Set(delivery?.hideLmsUi || []);
        const setButton = (id, visible, enabled) => {
            const button = document.getElementById(id);
            button.hidden = !visible;
            button.disabled = !enabled || navigating || stopped;
        };
        setButton('sp-prev', !hide.has('previous'), !!nav?.canPrevious);
        setButton('sp-next', !hide.has('continue'), !!nav?.canContinue);
        // Nút Thoát luôn hiện (gói SCORM giấu giao diện LMS cũng không được làm mất lối ra).
        setButton('sp-exit', true, true);
    }

    function renderSub() {
        if (!session) return;
        const s = session.summary;
        const parts = [];
        if (session.isPreview) parts.push(`<span class="sp-preview-flag">${esc(t('scorm.xemThu', 'Xem thử'))}</span>`);
        parts.push(session.isCourse ? 'SCORM 2004 4th Edition' : `SCORM ${esc(session.version)}`);
        parts.push(completionLabel(s.completionStatus, s.successStatus));
        if (s.scorePercent != null) parts.push(`${esc(t('scorm.diem', 'Điểm'))} ${s.scorePercent}%`);
        parts.push(`<span class="sp-save" id="sp-save"></span>`);
        document.getElementById('sp-sub').innerHTML = parts.join(' · ');
    }

    function setSaveState(state, message) {
        const box = document.getElementById('sp-save');
        if (!box) return;
        box.classList.toggle('error', state === 'error');
        box.innerHTML = state === 'saving' ? `<i class="ph ph-cloud-arrow-up"></i> ${esc(t('scorm.dangLuu', 'Đang lưu...'))}`
            : state === 'saved' ? `<i class="ph ph-cloud-check"></i> ${esc(t('scorm.daLuu', 'Đã lưu'))}`
            : `<i class="ph ph-warning"></i> ${esc(message || t('scorm.chuaLuuDuoc', 'Chưa lưu được'))}`;
    }

    function renderToc() {
        const nav = session?.nav;
        if (!nav) return;
        const list = nav.activityList.filter((a, i) => i > 0 && a.isVisible && !a.isHiddenFromChoice);
        tocBox.innerHTML = `<div class="sp-toc-head">${esc(t('scorm.mucLuc', 'Mục lục'))}${session.isCourse ? progressHtml() : ''}</div>`
            + (list.length ? list.map(nodeHtml).join('') : emptyHtml(t('scorm.khongCoMuc', 'Không có mục nào.')));
    }

    /** Tiến độ khóa học trong đầu mục lục: số mục lá đã xong / tổng số mục lá đang có mặt. */
    function progressHtml() {
        const leafList = session.nav.activityList.filter((a, i) => i > 0 && a.isLeaf && a.isVisible);
        if (!leafList.length) return '';
        const doneCount = leafList.filter(a => a.completion === 'completed' && a.success !== 'failed').length;
        return `<div style="margin-top:8px;text-transform:none;letter-spacing:0">${progressBar(Math.round(doneCount * 100 / leafList.length),
            { label: esc(`${doneCount}/${leafList.length} ${t('scorm.mucDaXong', 'mục đã xong')}`) })}</div>`;
    }

    function nodeHtml(a) {
        const indent = Math.max(0, a.depth - 1) * 16;
        const icon = a.success === 'failed' ? '<span class="ic fail"><i class="ph-fill ph-x-circle"></i></span>'
            : a.completion === 'completed' ? '<span class="ic done"><i class="ph-fill ph-check-circle"></i></span>'
            : a.isDisabled ? '<span class="ic lock"><i class="ph ph-lock-simple"></i></span>'
            : a.completion === 'incomplete' || a.isSuspended ? '<span class="ic doing"><i class="ph-fill ph-circle-half"></i></span>'
            : `<span class="ic lock"><i class="ph ph-${a.isLeaf ? 'circle' : 'folder-simple'}"></i></span>`;
        const score = a.scoreScaled != null && a.isLeaf ? `<span class="score">${Math.round(a.scoreScaled * 100)}%</span>` : '';
        const canChoose = a.canChoose && !navigating;
        return `<button type="button" class="sp-node ${a.isLeaf ? '' : 'cluster'} ${a.isCurrent ? 'current' : ''}" style="padding-left:${10 + indent}px"
                    ${canChoose ? `data-choice="${esc(a.id)}"` : 'disabled'} ${a.isCurrent ? 'aria-current="true"' : ''}
                    title="${esc(a.title)}">${icon}<span class="label">${esc(a.title)}</span>${score}</button>`;
    }

    function completionLabel(completion, success) {
        if (success === 'passed') return `<span style="color:var(--success);font-weight:700">${esc(t('scorm.dat', 'Đạt'))}</span>`;
        if (success === 'failed') return `<span style="color:var(--danger);font-weight:700">${esc(t('scorm.chuaDat', 'Chưa đạt'))}</span>`;
        if (completion === 'completed') return `<span style="color:var(--success);font-weight:700">${esc(t('scorm.hoanThanh', 'Hoàn thành'))}</span>`;
        if (completion === 'incomplete') return esc(t('scorm.dangHoc', 'Đang học'));
        return esc(t('scorm.chuaHoc', 'Chưa học'));
    }

    function showOverlay(html) { overlay.innerHTML = html; overlay.classList.remove('hidden'); }
    function hideOverlay() { overlay.classList.add('hidden'); overlay.innerHTML = ''; }

    function panelHtml(icon, title, text, actionHtml) {
        return `<div class="sp-panel"><div class="big-icon"><i class="ph ${icon}"></i></div>
            <h2>${esc(title)}</h2>${text ? `<p>${esc(text)}</p>` : ''}
            <div class="row-actions">${actionHtml || ''}</div></div>`;
    }

    function summaryHtml(s) {
        const minute = Math.round((s.totalTimeSecond || 0) / 60);
        return `<div class="sp-summary">
            <div class="tile"><div class="k">${esc(t('scorm.ketQua', 'Kết quả'))}</div><div class="v">${completionLabel(s.completionStatus, s.successStatus)}</div></div>
            <div class="tile"><div class="k">${esc(t('scorm.diem', 'Điểm'))}</div><div class="v">${s.scorePercent != null ? s.scorePercent + '%' : '—'}</div></div>
            <div class="tile"><div class="k">${esc(t('scorm.thoiGianHoc', 'Thời gian học'))}</div><div class="v">${minute} ${esc(t('scorm.phut', 'phút'))}</div></div>
        </div>`;
    }

    function endPanelHtml(dto) {
        const s = dto.summary;
        const done = !!s.completedAt;
        return `<div class="sp-panel">
            <div class="big-icon"><i class="ph ${done ? 'ph-seal-check' : s.isSuspended ? 'ph-floppy-disk' : 'ph-flag-checkered'}" style="${done ? 'color:var(--success)' : ''}"></i></div>
            <h2>${esc(done ? (session?.isCourse ? t('scorm.daHoanThanhKhoaHoc', 'Bạn đã hoàn thành khóa học') : t('scorm.daHoanThanhGoi', 'Bạn đã hoàn thành nội dung này'))
                : s.isSuspended ? t('scorm.daLuuChoDangHoc', 'Đã lưu chỗ đang học') : t('scorm.daKetThucPhien', 'Đã kết thúc phiên học'))}</h2>
            ${dto.message ? `<p>${esc(dto.message)}</p>` : ''}
            ${summaryHtml(s)}
            <div class="row-actions">
                <button type="button" class="btn" data-overlay="resume"><i class="ph ph-play"></i> ${esc(s.isSuspended ? t('scorm.hocTiep', 'Học tiếp') : t('scorm.moLai', 'Mở lại'))}</button>
                <button type="button" class="btn ghost" data-overlay="restart"><i class="ph ph-arrow-counter-clockwise"></i> ${esc(t('scorm.hocLaiTuDau', 'Học lại từ đầu'))}</button>
                <a class="btn ghost" href="${backUrl}"><i class="ph ph-arrow-left"></i> ${esc(backLabel)}</a>
            </div></div>`;
    }

    function messagePanelHtml(dto) {
        return `<div class="sp-panel">
            <div class="big-icon"><i class="ph ph-signpost"></i></div>
            <h2>${esc(t('scorm.chonMucTiepTheo', 'Chọn nội dung tiếp theo'))}</h2>
            <p>${esc(dto.message || t('scorm.chonMucTrongMucLuc', 'Chọn một mục trong mục lục hoặc bấm Tiếp để học tiếp.'))}</p>
            ${summaryHtml(dto.summary)}
            <div class="row-actions">
                ${dto.nav.canContinue ? `<button type="button" class="btn" data-overlay="continue">${esc(t('scorm.tiep', 'Tiếp'))} <i class="ph ph-caret-right"></i></button>` : ''}
                <button type="button" class="btn ghost" data-overlay="toc"><i class="ph ph-list"></i> ${esc(t('scorm.moMucLuc', 'Mở mục lục'))}</button>
            </div></div>`;
    }

    function bindOverlay() {
        overlay.querySelectorAll('[data-overlay]').forEach(button => button.addEventListener('click', async () => {
            const action = button.dataset.overlay;
            if (action === 'resume') { showOverlay(`<div class="sp-panel">${skeletonHtml('detail', { lines: 3 })}</div>`); await launch(false, null); }
            else if (action === 'restart') {
                if (!await confirmDialog(t('scorm.xacNhanHocLai', 'Học lại từ đầu? Kết quả đã đạt trước đó vẫn được giữ trên lộ trình.'),
                    { okText: t('scorm.hocLaiTuDau', 'Học lại từ đầu') })) return;
                showOverlay(`<div class="sp-panel">${skeletonHtml('detail', { lines: 3 })}</div>`);
                await launch(true, null);
            }
            else if (action === 'continue') requestNavigation('continue');
            else if (action === 'toc') shell.classList.remove('toc-hidden');
        }));
    }

    function safeParse(text) { try { return JSON.parse(text); } catch { return null; } }
})();
