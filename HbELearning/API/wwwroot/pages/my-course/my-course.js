// File: wwwroot/pages/my-course/my-course.js
(async function () {
    await Lang.ready;

    if (!Auth.require()) return;
    mountLayout('my-course', t('menu.my-course', 'Khóa học của tôi'), t('my-course.theoDoiTienDoVaTiepTucHoc', 'Theo dõi tiến độ và tiếp tục học'));

    // ===== Trạng thái trang — khai báo HẾT ở đây, trước lệnh await đầu tiên bên dưới. Hàm nào chạy sớm (handler
    // bấm nút, lời nhắc tải về trước danh sách khóa học...) mà đụng một const khai báo sau lệnh await thì dính
    // Temporal Dead Zone: ReferenceError ngay trong hàm đó và phần trang ấy trắng trơn, không báo gì. =====
    const DAY_MS = 24 * 60 * 60 * 1000;
    /** Còn từ chừng này ngày trở xuống thì hạn chót được tô cam ("sắp đến hạn"). */
    const SOON_DAYS = 7;
    const DEFAULT_FILTER = 'todo';

    /** Nhóm khóa học theo mức gấp, theo đúng thứ tự hiện trên trang.
     *  Khóa nhóm cũng là TÊN CLASS của thẻ (mc-item / mc-group-head) nên không được trùng class toàn cục: nhóm này từng
     *  tên 'progress', trùng .progress của thanh tiến độ — .progress > span tô mọi thẻ span con (dòng "Chưa có đánh giá",
     *  số đếm ở tiêu đề nhóm) thành một thanh xanh cao 100%. */
    const GROUP_LIST = [
        { key: 'overdue',   icon: 'ph-fill ph-warning-circle',          label: t('my-course.nhomQuaHan', 'Quá hạn') },
        { key: 'learning',  icon: 'ph-fill ph-play-circle',             label: t('dashboard.inProgress', 'Đang học') },
        { key: 'new',       icon: 'ph-fill ph-circle-dashed',           label: t('dashboard.notStarted', 'Chưa bắt đầu') },
        { key: 'failed',    icon: 'ph-fill ph-arrow-counter-clockwise', label: t('my-course.nhomChuaDat', 'Chưa đạt — cần học lại') },
        { key: 'done',      icon: 'ph-fill ph-seal-check',              label: t('index.stat-enroll', 'Hoàn thành') },
        { key: 'cancelled', icon: 'ph-fill ph-prohibit',                label: t('online-class.daHuy', 'Đã hủy') },
    ];
    /** Bộ lọc → các nhóm nó gồm. "Cần học" là mọi thứ còn phải làm, đó là câu hỏi người học mang tới trang này. */
    const FILTER_LIST = [
        { key: 'todo', icon: 'ph-book-open',   label: t('my-course.locCanHoc', 'Cần học'),   groupList: ['overdue', 'learning', 'new', 'failed'] },
        { key: 'done', icon: 'ph-seal-check',  label: t('dashboard.completed', 'Đã hoàn thành'), groupList: ['done'] },
        { key: 'all',  icon: 'ph-squares-four', label: t('dashboard.tatCa', 'Tất cả'),       groupList: GROUP_LIST.map(g => g.key) },
    ];

    const REPEAT_LABEL = {
        None: t('my-course.lapKhongNgan', 'Không lặp'), Daily: t('my-course.lapHangNgay', 'Hằng ngày'),
        Weekdays: t('my-course.lapNgayThuong', 'Thứ 2 → Thứ 6'), Weekly: t('my-course.lapHangTuan', 'Hằng tuần'),
    };
    /** DTO trả chu kỳ về dạng chữ, còn ô chọn và thân JSON gửi lên dùng số — đây là chỗ đổi qua lại. */
    const REPEAT_VALUE = { None: 0, Daily: 1, Weekdays: 2, Weekly: 3 };

    const listBox = document.getElementById('course-list');
    const keywordInput = document.getElementById('mc-keyword');
    const scheduleCache = new Map();

    let enrollmentList = [];
    let enrollmentLoaded = false;
    let reminderList = [];
    let availableList = [];
    /** Id lời nhắc đang sửa; null nghĩa là ô đang mở để tạo mới. */
    let editingReminderId = null;
    /** Tháng đang xem trên lịch — luôn là ngày mùng 1, cộng trừ tháng từ ngày 31 là nhảy cóc qua tháng 30 ngày. */
    let calendarDate = new Date(new Date().getFullYear(), new Date().getMonth(), 1);
    // Từ khóa gõ ở ô tìm kiếm trên thanh trên được chuyển sang đây qua địa chỉ (?keyword=). Có từ khóa thì mặc
    // định xem "Tất cả" — đang tìm một khóa cụ thể mà nó đã hoàn thành thì bộ lọc "Cần học" giấu mất kết quả.
    let keyword = (qs('keyword') || '').trim();
    let currentFilter = FILTER_LIST.some(f => f.key === qs('show')) ? qs('show') : (keyword ? 'all' : DEFAULT_FILTER);

    // ===== Gắn sự kiện =====
    keywordInput.value = keyword;
    keywordInput.addEventListener('input', () => {
        keyword = keywordInput.value.trim();
        setUrlParam('keyword', keyword);
        renderCourseList();
    });

    document.querySelectorAll('#mc-tabs [data-tab]').forEach(b => b.addEventListener('click', () => openTab(b.dataset.tab)));
    document.getElementById('btn-cal-prev').addEventListener('click', () => shiftMonth(-1));
    document.getElementById('btn-cal-next').addEventListener('click', () => shiftMonth(1));
    document.getElementById('btn-cal-today').addEventListener('click', goToday);
    document.getElementById('btn-add-reminder').addEventListener('click', () => openReminderForm());
    document.getElementById('btn-side-add-reminder').addEventListener('click', () => openReminderForm());
    document.getElementById('btn-save-reminder').addEventListener('click', saveReminder);
    document.getElementById('btn-enable-desktop-notification').addEventListener('click', async () => {
        await requestDesktopNotificationPermission();
        syncDesktopNotificationButton();
    });
    syncDesktopNotificationButton();

    // Thẻ đang mở nằm trên địa chỉ trang (?tab=) — tải lại trang, kể cả khi đổi ngôn ngữ, vẫn đứng nguyên thẻ đó.
    const startTab = tabFromUrl('course');
    showTab(startTab, 'course');

    // ===== Tải dữ liệu: ba nguồn độc lập, gọi song song, nguồn nào hỏng thì chỉ khối của nó báo lỗi =====
    listBox.innerHTML = skeletonHtml('list', { count: 4, avatar: false });
    await Promise.all([loadEnrollment(), loadReminderList(), loadAvailable()]);
    if (startTab === 'schedule') renderCalendar();

    // Thông báo "có trả lời / bình luận mới" dẫn về đây kèm ?feedback=<courseId> — mở thẳng hộp phản hồi của khóa đó.
    const feedbackCourseId = +(qs('feedback') || 0);
    if (feedbackCourseId)
        openFeedback(feedbackCourseId, enrollmentList.find(x => x.courseId === feedbackCourseId)?.courseTitle || '');

    function openTab(tab) {
        showTab(tab, 'course');
        if (tab === 'schedule') renderCalendar();
    }

    // ==================== THẺ KHÓA HỌC ====================

    async function loadEnrollment() {
        try {
            enrollmentList = await API.get('/enrollment/my');
            enrollmentLoaded = true;
        } catch (ex) {
            listBox.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning-circle"></i>');
            document.getElementById('mc-overview').innerHTML = '';
            document.getElementById('mc-deadline').innerHTML = '';
            return;
        }
        renderCourseList();
        renderOverview();
        renderDeadline();
    }

    /** Số ngày còn lại tới hạn, tính theo NGÀY LỊCH (hôm nay = 0, hôm qua = -1). null = không có hạn. */
    function daysLeft(x) {
        if (!x.dueDate) return null;
        const due = new Date(x.dueDate);
        const now = new Date();
        return Math.round((new Date(due.getFullYear(), due.getMonth(), due.getDate())
            - new Date(now.getFullYear(), now.getMonth(), now.getDate())) / DAY_MS);
    }

    // Khai báo function chứ không phải const arrow: các hàm này chạy ngay trong lúc thân trang còn dừng ở lệnh
    // await Promise.all(...) phía trên, khi đó một const khai báo ở dưới này vẫn nằm trong Temporal Dead Zone.
    function isOpen(x) { return x.status !== 'Completed' && x.status !== 'Cancelled'; }
    function isOverdue(x) { return isOpen(x) && (x.isOverdue || (daysLeft(x) ?? 0) < 0); }

    function groupOf(x) {
        if (x.status === 'Completed') return 'done';
        if (x.status === 'Cancelled') return 'cancelled';
        if (isOverdue(x)) return 'overdue';
        if (x.status === 'Failed') return 'failed';
        if (x.status === 'InProgress' || (Number(x.progressPercent) || 0) > 0) return 'learning';
        return 'new';
    }

    function matchKeyword(x) {
        return !keyword || `${x.courseTitle} ${x.courseCode}`.toLowerCase().includes(keyword.toLowerCase());
    }

    /** Trong một nhóm: hạn gần nhất lên đầu (không có hạn xuống cuối), cùng hạn thì khóa học dở nhiều hơn lên
     *  trước. Nhóm Hoàn thành thì khóa vừa xong lên đầu. */
    function compareInGroup(a, b) {
        if (a.status === 'Completed' && b.status === 'Completed')
            return new Date(b.completedAt || 0) - new Date(a.completedAt || 0);
        const dueA = a.dueDate ? new Date(a.dueDate).getTime() : Infinity;
        const dueB = b.dueDate ? new Date(b.dueDate).getTime() : Infinity;
        if (dueA !== dueB) return dueA - dueB;
        return (Number(b.progressPercent) || 0) - (Number(a.progressPercent) || 0);
    }

    function renderFilterChips() {
        const visibleList = enrollmentList.filter(matchKeyword);
        const box = document.getElementById('mc-filter');
        box.innerHTML = FILTER_LIST.map(f => {
            const count = visibleList.filter(x => f.groupList.includes(groupOf(x))).length;
            return `<button type="button" class="dash-chip ${f.key === currentFilter ? 'active' : ''}" data-filter="${f.key}"
                            aria-pressed="${f.key === currentFilter}">
                <i class="ph ${f.icon}"></i> ${esc(f.label)} <span class="count">${count}</span></button>`;
        }).join('');
        box.querySelectorAll('[data-filter]').forEach(b => b.addEventListener('click', () => {
            currentFilter = b.dataset.filter;
            setUrlParam('show', currentFilter === DEFAULT_FILTER ? '' : currentFilter);
            renderCourseList();
        }));
    }

    function renderCourseList() {
        if (!enrollmentLoaded) return;
        renderFilterChips();

        const filter = FILTER_LIST.find(f => f.key === currentFilter);
        const visibleList = enrollmentList.filter(x => matchKeyword(x) && filter.groupList.includes(groupOf(x)));

        if (!visibleList.length) {
            listBox.innerHTML = emptyListHtml();
            listBox.querySelector('[data-goto]')?.addEventListener('click', (e) => openTab(e.currentTarget.dataset.goto));
            listBox.querySelector('[data-show-all]')?.addEventListener('click', () => {
                currentFilter = 'all';
                setUrlParam('show', 'all');
                renderCourseList();
            });
            return;
        }

        listBox.innerHTML = GROUP_LIST
            .map(group => ({ group, itemList: visibleList.filter(x => groupOf(x) === group.key).sort(compareInGroup) }))
            .filter(x => x.itemList.length)
            .map(({ group, itemList }) => `
                <section class="mc-group">
                    <h4 class="mc-group-head ${group.key}"><i class="${group.icon}"></i> ${esc(group.label)}
                        <span class="n">${itemList.length}</span></h4>
                    <div class="mc-group-list">${itemList.map(itemHtml).join('')}</div>
                </section>`).join('');

        listBox.querySelectorAll('[data-scoreboard]').forEach(b => b.addEventListener('click', () => {
            const enrollment = enrollmentList.find(x => x.id === +b.dataset.scoreboard);
            openScoreboard(b.dataset.scoreboard, enrollment?.courseTitle || '');
        }));
        listBox.querySelectorAll('[data-feedback]').forEach(b => b.addEventListener('click', () =>
            openFeedback(+b.dataset.feedback, b.dataset.title || '')));
    }

    function openFeedback(courseId, courseTitle) {
        document.getElementById('feedback-title').textContent = courseTitle
            ? `${t('feedback.tieuDe', 'Phản hồi khóa học')} — ${courseTitle}` : t('feedback.tieuDe', 'Phản hồi khóa học');
        openModal('modal-feedback');
        mountCourseFeedback({ container: document.getElementById('feedback-body'), courseId });
    }

    function emptyListHtml() {
        if (!enrollmentList.length) {
            const cta = availableList.length
                ? `<div class="mc-empty-cta"><button type="button" class="btn sm" data-goto="suggest">
                       <i class="ph ph-sparkle"></i> ${esc(t('my-course.xemGoiY', 'Xem khóa học gợi ý cho bạn'))}</button></div>`
                : '';
            return emptyHtml(t('account.chuaCoKhoaHocNaoDuocPhanCong', 'Chưa có khóa học nào được phân công.'), '<i class="ph ph-folder-open"></i>')
                .replace(/<\/div>$/, `${cta}</div>`);
        }
        if (keyword)
            return emptyHtml(t('my-course.khongKhopTuKhoa', 'Không có khóa học nào khớp từ khóa "{keyword}".', { keyword }), '<i class="ph ph-magnifying-glass"></i>');
        if (currentFilter === 'todo')
            return emptyHtml(t('my-course.daHocXongHet', 'Bạn đã học xong mọi khóa học được giao.'), '<i class="ph ph-confetti"></i>')
                .replace(/<\/div>$/, `<div class="mc-empty-cta"><button type="button" class="btn ghost sm" data-show-all>
                    ${esc(t('my-course.xemTatCa', 'Xem tất cả khóa học'))}</button></div></div>`);
        return emptyHtml(t('my-course.chuaHoanThanhKhoaNao', 'Bạn chưa hoàn thành khóa học nào.'), '<i class="ph ph-seal-check"></i>');
    }

    function itemHtml(x) {
        const percent = Math.round(Number(x.progressPercent) || 0);
        const group = groupOf(x);
        const href = `/pages/learning/scorm-player.html?enrollmentId=${x.id}`;
        const [ctaLabel, ctaIcon, ctaClass] = x.status === 'Completed'
            ? [t('my-course.xemLai', 'Xem lại'), 'ph-arrow-counter-clockwise', 'ghost']
            : percent > 0
                ? [t('my-course.hocTiep', 'Học tiếp'), 'ph-play', '']
                : [t('my-course.batDau', 'Bắt đầu'), 'ph-play', ''];
        const scoreLabel = t('my-course.bangDiem', 'Bảng điểm');
        const feedbackLabel = x.status === 'Completed'
            ? t('feedback.danhGiaVaBinhLuan', 'Đánh giá & bình luận')
            : t('feedback.binhLuan', 'Bình luận');
        const certLabel = t('my-course.xemChungNhan', 'Xem chứng nhận');

        return `<article class="mc-item ${group}">
            <a class="mc-thumb-link" href="${href}" tabindex="-1" aria-hidden="true">${courseThumbHtml(x.courseTitle, x.thumbnailUrl)}</a>
            <div class="mc-main">
                <a class="mc-title" href="${href}">${esc(x.courseTitle)}</a>
                <div class="mc-meta">
                    <span class="code">${esc(x.courseCode)}</span>
                    ${dueHtml(x)}
                    ${x.finalScore != null ? `<span><i class="ph ph-medal"></i> ${esc(t('my-course.diem', 'Điểm {score}', { score: x.finalScore }))}</span>` : ''}
                </div>
                ${x.status === 'Cancelled' ? '' : `<div class="mc-prog">${progressBar(percent)}</div>`}
            </div>
            <div class="mc-actions">
                ${x.status === 'Cancelled' ? '' : `<button type="button" class="btn ghost sm icon" data-feedback="${x.courseId}" data-title="${esc(x.courseTitle)}"
                    title="${esc(feedbackLabel)}" aria-label="${esc(feedbackLabel)}"><i class="ph ph-chat-circle-text"></i></button>`}
                <button type="button" class="btn ghost sm icon" data-scoreboard="${x.id}" title="${esc(scoreLabel)}" aria-label="${esc(scoreLabel)}">
                    <i class="ph ph-chart-bar"></i></button>
                ${x.hasCertificate ? `<a class="btn ghost sm icon" href="/pages/certificate/certificate.html" title="${esc(certLabel)}" aria-label="${esc(certLabel)}">
                    <i class="ph ph-seal-check"></i></a>` : ''}
                ${x.status === 'Cancelled' ? '' : `<a class="btn sm ${ctaClass}" href="${href}"><i class="ph-bold ${ctaIcon}"></i> ${esc(ctaLabel)}</a>`}
            </div>
            ${courseRatingHtml(x.courseAverageRating, x.courseReviewCount)}
        </article>`;
    }

    /** Dòng hạn chót của một khóa học — đỏ khi đã trễ, cam khi còn ≤ 7 ngày, xám khi còn xa. */
    function dueHtml(x) {
        if (x.status === 'Completed')
            return x.completedAt ? `<span class="mc-due ok"><i class="ph-fill ph-check-circle"></i>
                ${esc(t('my-course.hoanThanhNgay', 'Hoàn thành {date}', { date: formatDate(x.completedAt) }))}</span>` : '';
        if (x.status === 'Cancelled') return '';

        const days = daysLeft(x);
        if (days === null) return '';
        if (isOverdue(x))
            return `<span class="mc-due bad"><i class="ph-fill ph-warning-circle"></i> ${esc(days < 0
                ? t('my-course.quaHanNgay', 'Quá hạn {n} ngày', { n: -days })
                : t('dashboard.overdue', 'Quá hạn'))}</span>`;
        if (days === 0) return `<span class="mc-due bad"><i class="ph-fill ph-clock"></i> ${esc(t('my-course.hanHomNay', 'Hạn chót hôm nay'))}</span>`;
        if (days <= SOON_DAYS) return `<span class="mc-due warn"><i class="ph-fill ph-clock"></i> ${esc(t('my-course.conNgay', 'Còn {n} ngày', { n: days }))}</span>`;
        return `<span><i class="ph ph-calendar-blank"></i> ${esc(t('my-course.hanNgay', 'Hạn {date}', { date: formatDate(x.dueDate) }))}</span>`;
    }

    function renderOverview() {
        const activeList = enrollmentList.filter(x => x.status !== 'Cancelled');
        const doneCount = activeList.filter(x => x.status === 'Completed').length;
        const progressCount = activeList.filter(x => ['learning', 'overdue', 'failed'].includes(groupOf(x))).length;
        const newCount = activeList.filter(x => groupOf(x) === 'new').length;
        const certificateCount = activeList.filter(x => x.hasCertificate).length;
        const percent = activeList.length ? Math.round(doneCount * 100 / activeList.length) : 0;

        document.getElementById('mc-overview').innerHTML = activeList.length ? `
            <div class="mc-overview-num"><b>${doneCount}</b><span>/ ${activeList.length}</span></div>
            <div class="mc-overview-sub">${esc(t('my-course.khoaDaHoanThanh', 'khóa học đã hoàn thành'))}</div>
            <div class="mc-overview-bar">${progressBar(percent)}</div>
            <div class="mc-overview-row"><span><i class="ph ph-play-circle"></i> ${esc(t('my-course.dangHocDo', 'Đang học dở'))}</span><b>${progressCount}</b></div>
            <div class="mc-overview-row"><span><i class="ph ph-circle-dashed"></i> ${esc(t('dashboard.notStarted', 'Chưa bắt đầu'))}</span><b>${newCount}</b></div>
            <div class="mc-overview-row"><span><i class="ph ph-seal-check"></i> ${esc(t('my-course.chungNhanDatDuoc', 'Chứng nhận đạt được'))}</span>
                ${certificateCount ? `<a href="/pages/certificate/certificate.html"><b>${certificateCount}</b> <i class="ph ph-arrow-right"></i></a>` : '<b>0</b>'}</div>`
            : `<div class="mc-side-empty">${esc(t('my-course.chuaCoSoLieu', 'Bắt đầu một khóa học là tiến độ hiện ở đây.'))}</div>`;
    }

    /** Hạn chót của các khóa còn phải học, gần nhất lên đầu — gồm cả khóa đã trễ hạn. */
    function renderDeadline() {
        const list = enrollmentList.filter(x => isOpen(x) && x.dueDate)
            .sort((a, b) => new Date(a.dueDate) - new Date(b.dueDate))
            .slice(0, 5);
        const box = document.getElementById('mc-deadline');

        if (!list.length) {
            box.innerHTML = `<div class="mc-side-empty">${esc(t('my-course.khongCoHanChot', 'Không có hạn chót nào — bạn học theo nhịp của mình.'))}</div>`;
            return;
        }

        box.innerHTML = `<div class="mc-dl">${list.map(x => {
            const days = daysLeft(x);
            const due = new Date(x.dueDate);
            const tone = isOverdue(x) || days === 0 ? 'bad' : days <= SOON_DAYS ? 'warn' : '';
            const note = isOverdue(x)
                ? (days < 0 ? t('my-course.quaHanNgay', 'Quá hạn {n} ngày', { n: -days }) : t('dashboard.overdue', 'Quá hạn'))
                : days === 0 ? t('my-course.hanHomNay', 'Hạn chót hôm nay')
                : t('my-course.conNgay', 'Còn {n} ngày', { n: days });
            return `<a href="/pages/learning/scorm-player.html?enrollmentId=${x.id}">
                <span class="mc-date ${tone}"><b>${due.getDate()}</b><span>${esc(due.toLocaleDateString(Lang.locale, { month: 'short' }))}</span></span>
                <span class="mc-dl-text"><b>${esc(x.courseTitle)}</b><small class="${tone}">${esc(note)}</small></span>
            </a>`;
        }).join('')}</div>`;
    }

    async function openScoreboard(enrollmentId, courseTitle) {
        document.getElementById('scoreboard-title').textContent = courseTitle
            ? `${t('my-course.bangDiem', 'Bảng điểm')} — ${courseTitle}` : t('my-course.bangDiem', 'Bảng điểm');
        const body = document.getElementById('scoreboard-body');
        body.innerHTML = skeletonHtml('list', { count: 3, avatar: false });
        openModal('modal-scoreboard');

        try {
            const detail = await API.get(`/enrollment/${enrollmentId}`);
            const quizList = detail.pathList.filter(x => x.itemType === 'Quiz');

            body.innerHTML = quizList.length ? `
                <div class="table-wrap"><table>
                    <thead><tr>
                        <th>${esc(t('common.baiKiemTra', 'Bài kiểm tra'))}</th>
                        <th class="text-center">${esc(t('my-course.diemCaoNhat', 'Điểm cao nhất'))}</th>
                        <th class="text-center">${esc(t('my-course.ketQua', 'Kết quả'))}</th>
                        <th class="text-center">${esc(t('my-course.luotDaLam', 'Lượt đã làm'))}</th>
                    </tr></thead>
                    <tbody>${quizList.map(q => `<tr>
                        <td><b>${esc(q.title)}</b></td>
                        <td class="text-center">${q.bestScore != null ? `<b>${q.bestScore}</b>` : '—'}</td>
                        <td class="text-center">${q.status === 'Completed'
                            ? `<span class="badge green">${esc(t('common.dat', 'Đạt'))}</span>`
                            : q.attemptUsed > 0 ? `<span class="badge red">${esc(t('common.chuaDat', 'Chưa đạt'))}</span>`
                            : `<span class="badge gray">${esc(t('my-course.chuaLam', 'Chưa làm'))}</span>`}</td>
                        <td class="text-center">${q.attemptUsed} / ${q.maxAttempt}</td>
                    </tr>`).join('')}</tbody>
                </table></div>`
                : emptyHtml(t('my-course.khoaHocNayChuaCoBaiKiemTraNao', 'Khóa học này chưa có bài kiểm tra nào.'), '<i class="ph ph-exam"></i>');
        } catch (ex) {
            body.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning-circle"></i>');
        }
    }

    // ==================== THẺ LỊCH HỌC ====================

    function ymd(d) {
        return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    }

    function shiftMonth(delta) {
        calendarDate = new Date(calendarDate.getFullYear(), calendarDate.getMonth() + delta, 1);
        renderCalendar();
    }

    async function goToday() {
        const now = new Date();
        calendarDate = new Date(now.getFullYear(), now.getMonth(), 1);
        await renderCalendar();
        document.getElementById(`mc-day-${now.getDate()}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    async function loadSchedule(year, month) {
        const key = `${year}-${month}`;
        if (scheduleCache.has(key)) return scheduleCache.get(key);
        try {
            const list = await API.get(`/enrollment/my-schedule?from=${ymd(new Date(year, month, 1))}&to=${ymd(new Date(year, month + 1, 0))}`);
            scheduleCache.set(key, list);
            return list;
        } catch {
            return [];
        }
    }

    async function renderCalendar() {
        const box = document.getElementById('mc-agenda');
        const year = calendarDate.getFullYear();
        const month = calendarDate.getMonth();
        // Chỉ viết hoa chữ đầu: text-transform: capitalize viết hoa MỌI từ, thành "Tháng 8 Năm 2026".
        const monthLabel = new Date(year, month, 1).toLocaleDateString(Lang.locale, { month: 'long', year: 'numeric' });
        document.getElementById('mc-cal-label').textContent = monthLabel.charAt(0).toUpperCase() + monthLabel.slice(1);

        if (!scheduleCache.has(`${year}-${month}`)) box.innerHTML = skeletonHtml('list', { count: 4, avatar: false });
        const scheduleList = await loadSchedule(year, month);
        // Bấm qua tháng khác trong lúc đang chờ thì kết quả này đã cũ — bỏ, để lần gọi mới vẽ.
        if (calendarDate.getFullYear() !== year || calendarDate.getMonth() !== month) return;

        const eventByDay = new Map();
        const push = (date, event) => {
            if (date.getFullYear() !== year || date.getMonth() !== month) return;
            if (!eventByDay.has(date.getDate())) eventByDay.set(date.getDate(), []);
            eventByDay.get(date.getDate()).push(event);
        };

        const firstLessonByCourse = new Map();
        scheduleList.forEach(x => {
            const cur = firstLessonByCourse.get(x.enrollmentId);
            if (!cur || new Date(x.scheduledAt) < new Date(cur.scheduledAt)) firstLessonByCourse.set(x.enrollmentId, x);
        });

        firstLessonByCourse.forEach(x => push(new Date(x.scheduledAt), {
            kind: 'lesson',
            icon: 'ph-play-circle',
            title: t('my-course.baiHocDauTien', 'Bài học đầu tiên: {title}', { title: x.lessonTitle }),
            subtitle: t('my-course.khoaHoc', 'Khóa học: {title}', { title: x.courseTitle }),
            done: x.status === 'Completed',
            enrollmentId: x.enrollmentId,
        }));


        enrollmentList.filter(x => x.dueDate && x.status !== 'Cancelled').forEach(x => push(new Date(x.dueDate), {
            kind: 'due',
            icon: 'ph-flag-checkered',
            title: t('my-course.hanChotKhoa', 'Hạn chót: {title}', { title: x.courseTitle }),
            subtitle: t('my-course.yeuCauHoanThanhKhoa', 'Phải hoàn thành toàn bộ khóa học'),
            done: x.status === 'Completed',
            enrollmentId: x.id,
        }));

        if (!eventByDay.size) {
            box.innerHTML = emptyHtml(t('my-course.thangNayTrong', 'Tháng này chưa có khoá học hay hạn chót nào.'), '<i class="ph ph-calendar-blank"></i>');
            return;
        }

        const today = new Date();
        const isThisMonth = today.getFullYear() === year && today.getMonth() === month;

        box.innerHTML = `<div class="mc-agenda">${[...eventByDay.keys()].sort((a, b) => a - b).map(day => {
            const date = new Date(year, month, day);
            const isToday = isThisMonth && day === today.getDate();
            // Hạn chót lên trước bài học trong cùng một ngày: nó là mốc cứng, bài học thì học sớm muộn đều được.
            const eventList = eventByDay.get(day).sort((a, b) => (a.kind === 'due' ? 0 : 1) - (b.kind === 'due' ? 0 : 1));
            return `<div class="mc-day ${isToday ? 'today' : ''}" id="mc-day-${day}">
                <div class="mc-day-date"><b>${day}</b>
                    <span>${esc(isToday ? t('my-course.homNay', 'Hôm nay') : date.toLocaleDateString(Lang.locale, { weekday: 'short' }))}</span></div>
                <div class="mc-events">${eventList.map(e => eventHtml(e, day)).join('')}</div>
            </div>`;
        }).join('')}</div>`;

        box.querySelectorAll('[data-remind-title]').forEach(btn => btn.addEventListener('click', () => {
            // Ngày gửi qua ba con số rồi dựng lại tại chỗ: chuỗi "2026-09-08" bị trình duyệt hiểu là nửa đêm
            // giờ UTC, lệch múi giờ là nhảy sang ngày khác.
            const [y, m, d] = btn.dataset.remindDay.split(',').map(Number);
            openReminderForm({
                title: btn.dataset.remindTitle,
                enrollmentId: Number(btn.dataset.remindEnrollment) || null,
                remindAt: suggestedTime(new Date(y, m, d)),
            });
        }));

        function eventHtml(e, day) {
            const on = hasReminderFor(e);
            const bellLabel = on ? t('my-course.daCoNhacNho', 'Đã có nhắc nhở cho việc này') : t('my-course.nhacToiViecNay', 'Nhắc tôi học việc này');
            return `<div class="mc-event ${e.kind} ${e.done ? 'done' : ''}">
                <a href="/pages/learning/scorm-player.html?enrollmentId=${e.enrollmentId}">
                    <span class="ic"><i class="ph-fill ${e.icon}"></i></span>
                    <span class="tx"><b>${esc(e.title)}</b><span>${esc(e.subtitle)}</span></span>
                </a>
                <button type="button" class="btn ghost sm icon mc-bell ${on ? 'on' : ''}" title="${esc(bellLabel)}" aria-label="${esc(bellLabel)}"
                        data-remind-title="${esc(e.title)}" data-remind-enrollment="${e.enrollmentId}" data-remind-day="${year},${month},${day}">
                    <i class="ph-fill ph-alarm"></i></button>
            </div>`;
        }
    }

    // ==================== NHẮC NHỞ HỌC TẬP TỰ ĐẶT ====================
    // Người học tự hẹn giờ, máy chủ bắn thông báo đúng phút đó (Services/StudyReminderWorker.cs). Trang này
    // chỉ lo phần đặt/sửa/xóa — việc báo tới nơi là của chuông ở thanh trên cùng (wwwroot/js/app.js).

    /** Việc này đã có lời nhắc đang bật chưa — để tô sáng nút chuông trên lịch. */
    function hasReminderFor(event) {
        return reminderList.some(x => x.isActive
            && x.title === event.title
            && (x.enrollmentId ?? null) === (event.enrollmentId ?? null));
    }

    /** Giờ gợi ý sẵn khi mở ô đặt nhắc: 8h sáng của ngày trên lịch, hoặc tròn giờ kế tiếp nếu mốc đó đã qua. */
    function suggestedTime(dayDate) {
        const now = new Date();
        if (dayDate) {
            const morning = new Date(dayDate);
            morning.setHours(8, 0, 0, 0);
            if (morning > now) return morning;
        }
        const nextHour = new Date(now.getTime() + 60 * 60 * 1000);
        nextHour.setMinutes(0, 0, 0);
        return nextHour;
    }

    /** Định dạng cho <input type="datetime-local"> — phải là giờ ĐỊA PHƯƠNG, dùng toISOString() là lệch múi giờ. */
    function toInputValue(date) {
        const pad = (n) => String(n).padStart(2, '0');
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
             + `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    }

    function syncDesktopNotificationButton() {
        // Chỉ mời bật khi trình duyệt còn đang hỏi được — đã bật rồi, hoặc đã chặn, thì cái nút chỉ là nhiễu.
        document.getElementById('btn-enable-desktop-notification')
            .classList.toggle('hidden', desktopNotificationState() !== 'default');
    }

    async function loadReminderList() {
        const box = document.getElementById('reminder-list');
        box.innerHTML = skeletonHtml('list', { count: 3, avatar: false });
        try {
            reminderList = await API.get('/studyreminder');
        } catch (ex) {
            box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning-circle"></i>');
            document.getElementById('mc-reminder-mini').innerHTML = '';
            return;
        }
        renderReminderList();
        renderReminderMini();
        // Chuông trên lịch sáng/tắt theo danh sách vừa lấy về.
        if (document.getElementById('tab-schedule').classList.contains('active')) renderCalendar();
    }

    function reminderMetaHtml(x, full) {
        return `<div class="meta">
            <span><i class="ph ph-clock"></i> ${esc(x.isActive ? '' : t('my-course.dangTat', 'Đang tắt') + ' · ')}${esc(formatDate(x.remindAt, true))}</span>
            ${full ? `<span><i class="ph ph-repeat"></i> ${esc(REPEAT_LABEL[x.repeat] || x.repeat)}</span>` : ''}
            ${full && x.courseTitle ? `<span><i class="ph ph-book-open"></i> ${esc(x.courseTitle)}</span>` : ''}
            ${full && x.sendEmail ? `<span><i class="ph ph-envelope-simple"></i> ${esc(t('my-course.coGuiEmail', 'Có gửi email'))}</span>` : ''}
        </div>`;
    }

    function renderReminderList() {
        const box = document.getElementById('reminder-list');
        if (!reminderList.length) {
            box.innerHTML = emptyHtml(t('my-course.chuaCoNhacNhoGoiY', 'Bạn chưa đặt lời nhắc nào. Bấm "Đặt nhắc nhở", hoặc bấm nút chuông trên một buổi học ở lịch.'),
                '<i class="ph ph-alarm"></i>');
            return;
        }

        const pauseLabel = t('my-course.tamTat', 'Tạm tắt'), resumeLabel = t('my-course.batLai', 'Bật lại');
        const editLabel = t('common.edit', 'Sửa'), deleteLabel = t('common.delete', 'Xóa');
        box.innerHTML = reminderList.map(x => `
            <div class="mc-reminder ${x.isActive ? '' : 'off'}">
                <span class="ic"><i class="ph-fill ${x.isActive ? 'ph-alarm' : 'ph-bell-slash'}"></i></span>
                <div class="tx"><b>${esc(x.title)}</b>${reminderMetaHtml(x, true)}</div>
                <div class="act">
                    <button type="button" class="btn ghost sm icon" data-reminder-toggle="${x.id}"
                            title="${esc(x.isActive ? pauseLabel : resumeLabel)}" aria-label="${esc(x.isActive ? pauseLabel : resumeLabel)}">
                        <i class="ph ${x.isActive ? 'ph-pause' : 'ph-play'}"></i></button>
                    <button type="button" class="btn ghost sm icon" data-reminder-edit="${x.id}" title="${esc(editLabel)}" aria-label="${esc(editLabel)}">
                        <i class="ph ph-pencil-simple"></i></button>
                    <button type="button" class="btn ghost danger sm icon" data-reminder-delete="${x.id}" title="${esc(deleteLabel)}" aria-label="${esc(deleteLabel)}">
                        <i class="ph ph-trash"></i></button>
                </div>
            </div>`).join('');

        box.querySelectorAll('[data-reminder-toggle]').forEach(btn =>
            btn.addEventListener('click', () => toggleReminder(+btn.dataset.reminderToggle)));
        box.querySelectorAll('[data-reminder-edit]').forEach(btn =>
            btn.addEventListener('click', () => {
                const item = reminderList.find(x => x.id === +btn.dataset.reminderEdit);
                if (item) openReminderForm(item);
            }));
        box.querySelectorAll('[data-reminder-delete]').forEach(btn =>
            btn.addEventListener('click', () => deleteReminder(+btn.dataset.reminderDelete)));
    }

    /** Ba lời nhắc đang bật gần nhất, ở cột phụ của thẻ Khóa học. */
    function renderReminderMini() {
        const box = document.getElementById('mc-reminder-mini');
        const list = reminderList.filter(x => x.isActive)
            .sort((a, b) => new Date(a.remindAt) - new Date(b.remindAt))
            .slice(0, 3);

        box.innerHTML = (list.length
            ? list.map(x => `<div class="mc-reminder mini">
                    <span class="ic"><i class="ph-fill ph-alarm"></i></span>
                    <div class="tx"><b>${esc(x.title)}</b>${reminderMetaHtml(x, false)}</div>
                </div>`).join('')
            : `<div class="mc-side-empty">${esc(t('my-course.chuaCoNhacNho', 'Chưa có nhắc nhở nào đang bật.'))}</div>`)
            + `<button type="button" class="btn ghost sm block mt" data-goto="schedule">
                   ${esc(t('my-course.quanLyLichNhacNho', 'Mở lịch & nhắc nhở'))} <i class="ph ph-arrow-right"></i></button>`;
        box.querySelector('[data-goto]').addEventListener('click', () => openTab('schedule'));
    }

    /** Mở ô đặt nhắc. Truyền vào một lời nhắc đã có (có id) để sửa, hoặc dữ liệu gợi ý sẵn để tạo mới. */
    function openReminderForm(prefill = {}) {
        editingReminderId = prefill.id ?? null;
        document.getElementById('reminder-modal-title').textContent = editingReminderId
            ? t('my-course.suaNhacNho', 'Sửa nhắc nhở học tập') : t('my-course.datNhacNhoHocTap', 'Đặt nhắc nhở học tập');

        const courseSelect = document.getElementById('reminder-enrollment');
        courseSelect.innerHTML = `<option value="">— ${esc(t('my-course.khongGanKhoaHoc', 'Không gắn khóa học'))} —</option>`
            + enrollmentList.filter(x => x.status !== 'Cancelled')
                .map(x => `<option value="${x.id}">${esc(x.courseTitle)}</option>`).join('');
        courseSelect.value = prefill.enrollmentId ? String(prefill.enrollmentId) : '';

        document.getElementById('reminder-title').value = prefill.title || '';
        document.getElementById('reminder-note').value = prefill.note || '';
        document.getElementById('reminder-repeat').value = String(REPEAT_VALUE[prefill.repeat] ?? 0);
        document.getElementById('reminder-email').checked = !!prefill.sendEmail;
        document.getElementById('reminder-at').value =
            toInputValue(prefill.remindAt ? new Date(prefill.remindAt) : suggestedTime(null));

        openModal('modal-reminder');
        setTimeout(() => document.getElementById('reminder-title').focus(), 50);
    }

    async function saveReminder() {
        const title = document.getElementById('reminder-title').value.trim();
        const remindAt = document.getElementById('reminder-at').value;
        const repeat = Number(document.getElementById('reminder-repeat').value);

        if (!title) return toastErr(t('my-course.nhapViecCanNhac', 'Hãy nhập việc cần nhắc.'));
        if (!remindAt) return toastErr(t('my-course.chonGioNhac', 'Hãy chọn giờ nhắc.'));
        // Chặn ngay tại chỗ cho lời nhắc một lần đặt vào quá khứ — máy chủ cũng chặn, nhưng báo ở đây thì
        // người dùng thấy liền mà không mất một vòng gọi mạng.
        if (repeat === REPEAT_VALUE.None && new Date(remindAt) <= new Date())
            return toastErr(t('my-course.gioNhacPhaiOTuongLai', 'Giờ nhắc phải nằm ở tương lai.'));

        const enrollmentValue = document.getElementById('reminder-enrollment').value;
        const body = {
            title,
            note: document.getElementById('reminder-note').value.trim() || null,
            // Gửi nguyên chuỗi giờ địa phương của ô nhập: máy chủ và người dùng ở cùng múi giờ,
            // đổi qua ISO có chữ Z là lệch mất mấy tiếng.
            remindAt,
            repeat,
            sendEmail: document.getElementById('reminder-email').checked,
            enrollmentId: enrollmentValue ? Number(enrollmentValue) : null,
        };

        try {
            if (editingReminderId) await API.put(`/studyreminder/${editingReminderId}`, body);
            else await API.post('/studyreminder', body);
            closeModal('modal-reminder');
            toastOk(t('my-course.daLuuNhacNho', 'Đã lưu nhắc nhở. Tới giờ hệ thống sẽ báo cho bạn.'));
            await loadReminderList();
            await offerDesktopNotification();
        } catch (ex) { toastErr(ex.message); }
    }

    async function toggleReminder(id) {
        try {
            await API.post(`/studyreminder/${id}/toggle`);
            await loadReminderList();
        } catch (ex) { toastErr(ex.message); }
    }

    async function deleteReminder(id) {
        if (!await confirmDialog(t('my-course.xoaNhacNho', 'Xóa lời nhắc này?'), { danger: true, okText: t('common.delete', 'Xóa') })) return;
        try {
            await API.del(`/studyreminder/${id}`);
            await loadReminderList();
        } catch (ex) { toastErr(ex.message); }
    }

    /** Vừa đặt lời nhắc đầu tiên là lúc hợp lý nhất để xin quyền hiện thông báo trên máy: người dùng vừa nói
     *  rõ họ muốn được nhắc, nên lời xin quyền không đến từ đâu rơi xuống. Chỉ hỏi một lần cho mỗi máy. */
    async function offerDesktopNotification() {
        if (desktopNotificationState() !== 'default') return;
        try {
            if (localStorage.getItem('hbe_reminder_permission_asked')) return;
            localStorage.setItem('hbe_reminder_permission_asked', '1');
        } catch { return; }
        await requestDesktopNotificationPermission();
        syncDesktopNotificationButton();
    }

    // ==================== THẺ GỢI Ý ====================

    async function loadAvailable() {
        const box = document.getElementById('available');
        box.innerHTML = skeletonHtml('cards', { count: 3 });
        try {
            availableList = await API.get('/course/my-available');
        } catch {
            box.innerHTML = emptyHtml(t('my-course.khongTaiDuocDanhSach', 'Không tải được danh sách.'), '<i class="ph ph-warning-circle"></i>');
            return;
        }

        const countBadge = document.getElementById('mc-suggest-count');
        countBadge.textContent = availableList.length;
        countBadge.classList.toggle('hidden', !availableList.length);
        // Danh sách khóa học trống có nút "Xem khóa học gợi ý" — chỉ vẽ được khi đã biết có gợi ý hay không.
        if (enrollmentLoaded && !enrollmentList.length) renderCourseList();

        if (!availableList.length) {
            box.innerHTML = emptyHtml(t('my-course.khongCoKhoaHocMoiPhuHopVoiPhon', 'Không có khóa học mới phù hợp với phòng ban / vị trí của bạn.'), '<i class="ph ph-confetti"></i>');
            return;
        }

        box.innerHTML = `<div class="mc-suggest-grid">${availableList.map(c => `
            <article class="mc-suggest">
                ${courseThumbHtml(c.title, c.thumbnailUrl)}
                <div class="mc-suggest-body">
                    <h4>${esc(c.title)}</h4>
                    <div class="mc-meta">
                        <span class="code">${esc(c.code)}</span>
                        ${c.isMandatory ? `<span class="badge orange">${esc(t('suggest.mandatory', 'Bắt buộc'))}</span>` : ''}
                    </div>
                    <div class="mc-meta">
                        <span><i class="ph ph-clock"></i> ${formatDuration(c.durationMinutes)}</span>
                        <span><i class="ph ph-list-dashes"></i> ${esc(t('my-course.soBaiHoc', '{n} bài học', { n: c.lessonCount }))}</span>
                    </div>
                    ${c.suggestReason ? `<div class="mc-reason"><i class="ph-fill ph-sparkle"></i><span>${esc(suggestReasonText(c.suggestReason))}</span></div>` : ''}
                    ${courseRatingHtml(c.averageRating, c.reviewCount)}
                    <button type="button" class="btn sm block" data-enroll="${c.id}">
                        <i class="ph-bold ph-plus-circle"></i> ${esc(t('suggest.enroll', 'Đăng ký học'))}</button>
                </div>
            </article>`).join('')}</div>`;

        box.querySelectorAll('[data-enroll]').forEach(b => b.addEventListener('click', async () => {
            const course = availableList.find(x => x.id === +b.dataset.enroll);
            b.disabled = true;
            if (await selfEnrollCourse(course.id, course.title)) {
                // Khóa vừa đăng ký chuyển từ Gợi ý sang danh sách Khóa học (và lên lịch học).
                scheduleCache.clear();
                await Promise.all([loadEnrollment(), loadAvailable()]);
            } else {
                b.disabled = false;
            }
        }));
    }
})();
