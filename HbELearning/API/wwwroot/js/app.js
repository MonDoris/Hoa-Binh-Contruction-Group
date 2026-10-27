// File: wwwroot/js/app.js
// Bố cục dùng chung, thông báo và các hàm tiện ích.
// ==================== THÔNG BÁO ====================
function toast(message, type = 'info') {
    let box = document.getElementById('toast-box');
    if (!box) {
        box = document.createElement('div');
        box.id = 'toast-box';
        document.body.appendChild(box);
    }
    const el = document.createElement('div');
    el.className = `toast ${type === 'error' ? 'err' : type === 'success' ? 'ok' : ''}`;
    el.textContent = message;
    box.appendChild(el);
    setTimeout(() => el.remove(), 4000);
}

const toastOk = (m) => toast(m, 'success');
const toastErr = (m) => toast(m, 'error');

// ==================== TIỆN ÍCH ====================
const esc = (value) => String(value ?? '')
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');

// Thẻ <phan_tich> trong câu trả lời AI (đã escape). Model 3B có lúc viết lệch (<phân_tích>, <phan tich>) — khớp rộng tay.
const AI_ANALYSIS_OPEN = /&lt;\s*ph[aâ]n[_ ]?t[ií]ch\s*&gt;/i;
const AI_ANALYSIS_CLOSE = /&lt;\s*\/\s*ph[aâ]n[_ ]?t[ií]ch\s*&gt;/i;

/** Câu trả lời của AI (chữ ĐÃ qua esc) -> HTML: bảng Markdown, **đậm**, `mã`, tiêu đề #, gạch đầu dòng, xuống dòng.
 *  Câu lệnh hệ thống dặn model kẻ bảng khi so sánh từ 3 thứ trở lên, nên bảng phải hiện thành bảng chứ không
 *  phải mấy dòng gạch đứng. Nhận chữ đã escape để model không bao giờ chèn được thẻ HTML; số nguồn [1] [2]
 *  thì mỗi trang tự xử lý TRƯỚC khi gọi (trang học viên xóa đi, trang quản trị tô lên).
 *  Đang stream thì dòng tiêu đề bảng hiện như chữ thường cho tới khi dòng |---| tới, rồi mới thành bảng.
 *
 *  Câu hỏi lập kế hoạch / xử lý sự cố được dặn viết phần <phan_tich> trước bản kế hoạch (ReasoningAnswerFormat
 *  trong OllamaChatService) — phần đó vào một khung "Phân tích" gập được. Chữ đã escape nên thẻ có dạng &lt;...&gt;.
 *  Đang stream thì thẻ đóng chưa tới: từ thẻ mở tới hết chữ vẫn nằm trong khung. */
function aiAnswerHtml(escapedText) {
    const open = escapedText.match(AI_ANALYSIS_OPEN);
    if (!open) return aiAnswerBodyHtml(escapedText);

    const rest = escapedText.slice(open.index + open[0].length);
    const close = rest.match(AI_ANALYSIS_CLOSE);
    const before = escapedText.slice(0, open.index).trim();
    const inner = (close ? rest.slice(0, close.index) : rest).trim();
    const after = close ? rest.slice(close.index + close[0].length).trim() : '';

    return (before ? aiAnswerBodyHtml(before) : '')
        + `<details class="ai-analysis" open><summary>Phân tích</summary><div class="ai-analysis-body">${aiAnswerBodyHtml(inner)}</div></details>`
        + (after ? aiAnswerHtml(after) : '');
}

function aiAnswerBodyHtml(escapedText) {
    escapedText = escapedText
        // Thẻ đóng lẻ (model quên thẻ mở) không được hiện thành chữ.
        .replace(/&lt;\s*\/?\s*ph[aâ]n[_ ]?t[ií]ch\s*&gt;/gi, '')
        // Đang stream: thẻ mới tới nửa chừng ("<phan_") ở cuối thì giấu đi, token sau tới là thành khung.
        .replace(/&lt;\/?[a-zâí_]*$/i, '');

    const isRow = (line) => /^\s*\|/.test(line);
    const isDivider = (line) => /^\s*\|?(\s*:?-+:?\s*\|)+\s*(:?-+:?\s*)?$/.test(line);
    const cellsOf = (line) => line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(x => x.trim());

    const html = escapedText
        .replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>')
        .replace(/`([^`\n]+)`/g, '<code>$1</code>');

    const lineList = html.split('\n');
    const partList = [];   // { html, block } — block = bảng, không chèn <br> quanh nó
    for (let i = 0; i < lineList.length; i++) {
        if (isRow(lineList[i]) && isDivider(lineList[i + 1] ?? '')) {
            const head = cellsOf(lineList[i]);
            const rowList = [];
            for (i += 2; i < lineList.length && isRow(lineList[i]); i++) rowList.push(cellsOf(lineList[i]));
            i--;
            // Dòng trống ngay trước bảng: bỏ, bảng đã có lề riêng.
            if (partList.length && !partList.at(-1).block && partList.at(-1).html.trim() === '') partList.pop();
            partList.push({
                block: true,
                html: '<div class="ai-table-wrap"><table class="ai-table"><thead><tr>'
                    + head.map(c => `<th>${c}</th>`).join('') + '</tr></thead><tbody>'
                    + rowList.map(r => '<tr>' + head.map((_, k) => `<td>${r[k] ?? ''}</td>`).join('') + '</tr>').join('')
                    + '</tbody></table></div>'
            });
            continue;
        }
        const line = lineList[i];
        // Dòng trống ngay sau bảng: bỏ, cùng lý do.
        if (line.trim() === '' && partList.at(-1)?.block) continue;
        partList.push({
            block: false,
            html: line
                .replace(/^[ \t]*#{1,4}[ \t]+(.+)$/, '<strong>$1</strong>')
                .replace(/^[ \t]*[-*] /, '• ')
        });
    }

    return partList.map((part, i) =>
        (i > 0 && !part.block && !partList[i - 1].block ? '<br>' : '') + part.html).join('');
}

const qs = (name) => new URLSearchParams(location.search).get(name);

// ----- Thẻ (tab) đang mở nằm trên địa chỉ trang -----
// Ghi thẻ đang mở lên địa chỉ dạng ?tab=<khóa>, để tải lại trang — F5, hay đổi ngôn ngữ (Lang.set buộc phải tải
// lại trang) — mở lại đúng thẻ đang xem thay vì nhảy về thẻ đầu. Cùng cách shop.html / certificate.html đã làm.

/** Ghi thẻ lên địa chỉ trang. Thẻ mặc định thì bỏ hẳn tham số cho địa chỉ gọn. replaceState chứ không
 *  pushState: bấm Back vẫn về trang trước, không phải lùi qua từng thẻ đã bấm. */
function rememberTab(tab, defaultTab) {
    setUrlParam('tab', tab === defaultTab ? '' : tab);
}

/** Ghi một tham số lên địa chỉ trang (giá trị rỗng = gỡ hẳn tham số), không tạo thêm bước Back. Dùng cho bộ
 *  lọc, từ khóa... — cùng lý do với rememberTab: tải lại trang (F5, đổi ngôn ngữ) vẫn giữ nguyên thứ đang xem. */
function setUrlParam(name, value) {
    const url = new URL(location.href);
    if (value) url.searchParams.set(name, value); else url.searchParams.delete(name);
    history.replaceState(history.state, '', url);
}

/** Thẻ cần mở lúc vào trang: thẻ ghi trên địa chỉ nếu trang có nút của nó và nút đó đang hiện, không thì
 *  defaultTab. Thẻ bị ẩn theo vai trò (style.display = 'none') không mở được qua địa chỉ. */
function tabFromUrl(defaultTab) {
    const tab = qs('tab');
    const button = tab && [...document.querySelectorAll('.tabs button[data-tab]')].find(b => b.dataset.tab === tab);
    return button && button.style.display !== 'none' ? tab : defaultTab;
}

/** Mở một thẻ theo khuôn chung của các trang — nút `.tabs button[data-tab]` + khung `.tab-panel#tab-<khóa>` — và
 *  ghi nó lên địa chỉ trang. Chỉ đổi phần hiển thị; nạp dữ liệu cho thẻ là việc của trang. */
function showTab(tab, defaultTab) {
    document.querySelectorAll('.tabs button[data-tab]').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
    document.querySelectorAll('.tab-panel').forEach(p => p.classList.toggle('active', p.id === `tab-${tab}`));
    rememberTab(tab, defaultTab);
}

/** Đọc giá trị thật của một biến CSS. Dùng cho những thứ nhận màu qua JavaScript chứ không qua CSS —
 *  điển hình là biểu đồ Chart.js: ghi thẳng mã màu vào cấu hình biểu đồ thì đổi nền sáng/tối xong biểu đồ
 *  vẫn giữ nguyên bảng màu của nền cũ, còn đổi màu thương hiệu thì biểu đồ là chỗ duy nhất không đổi theo. */
const cssVar = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

/** Màu chữ ĐỌC ĐƯỢC trên một nền cho trước (#rgb hoặc #rrggbb): chữ trắng nếu nền đủ đậm, chữ tối nếu nền sáng.
 *  Dùng cho mọi nhãn/chip có màu nền do người dùng tự chọn (VD màu bậc hạng ở màn Xếp hạng) — ghi cứng chữ trắng thì
 *  khi Admin chọn nền trắng hay vàng nhạt, chữ biến mất. Khai báo function (được hoisting) chứ không phải const: trang
 *  nào cũng có thể gọi nó từ lúc dựng HTML đầu tiên, trước khi engine chạy tới dòng này.
 *  Ngưỡng 3:1 (độ tương phản WCAG cho chữ đậm) — dưới ngưỡng đó chữ trắng khó đọc nên chuyển sang chữ tối. Màu không
 *  nhận ra được thì giữ chữ trắng như trước. */
function readableTextColor(hex) {
    const match = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(String(hex ?? '').trim());
    if (!match) return '#fff';

    const full = match[1].length === 3 ? [...match[1]].map(c => c + c).join('') : match[1];
    const [r, g, b] = [0, 2, 4]
        .map(i => parseInt(full.slice(i, i + 2), 16) / 255)
        .map(c => c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
    const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

    const contrastWithWhite = 1.05 / (luminance + 0.05);
    return contrastWithWhite >= 3 ? '#fff' : '#1e293b';
}

function formatDate(value, withTime = false) {
    if (!value) return '—';
    const d = new Date(value);
    if (isNaN(d)) return '—';
    const date = d.toLocaleDateString(Lang.locale);
    return withTime ? `${date} ${d.toLocaleTimeString(Lang.locale, { hour: '2-digit', minute: '2-digit' })}` : date;
}

/**
 * Khoảng cách thời gian dạng nói thường: "vừa xong", "2 giờ trước", "3 ngày trước".
 *
 * Dùng cho thông báo thay vì ngày giờ đầy đủ: cái người đọc cần biết là việc đó MỚI hay CŨ, chứ không phải
 * nó xảy ra lúc 14:07 ngày nào. Quá một tuần thì quay về ngày cụ thể — "23 ngày trước" thì phải tự nhẩm.
 */
function timeAgo(value) {
    if (!value) return '';
    const second = Math.max(0, (Date.now() - new Date(value).getTime()) / 1000);

    if (second < 60) return t('time.justNow', 'vừa xong');
    if (second < 3600) return t('time.minuteAgo', '{n} phút trước', { n: Math.floor(second / 60) });
    if (second < 86400) return t('time.hourAgo', '{n} giờ trước', { n: Math.floor(second / 3600) });
    if (second < 7 * 86400) return t('time.dayAgo', '{n} ngày trước', { n: Math.floor(second / 86400) });
    return formatDate(value);
}

/** Thời lượng từ MILI GIÂY dạng "1h 12m" — viết tắt giống nhau ở mọi ngôn ngữ; dưới một phút là "<1m"; thiếu giá trị là "—". */
function formatSpanMs(ms) {
    if (ms == null) return '—';
    const minutes = Math.floor(ms / 60000);
    if (minutes < 1) return '<1m';
    return minutes >= 60 ? `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, '0')}m` : `${minutes}m`;
}

/** Đồng hồ từ MILI GIÂY: "mm:ss", hoặc "h:mm:ss" từ một giờ. Làm tròn XUỐNG giây (khớp với đồng hồ của trình phát video). */
function formatClockMs(ms) {
    const total = Math.max(0, Math.floor((ms || 0) / 1000));
    const h = Math.floor(total / 3600);
    const mm = String(Math.floor(total / 60) % 60).padStart(2, '0');
    const ss = String(total % 60).padStart(2, '0');
    return h ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

/** Dung lượng tệp dễ đọc: "12,3 MB". */
function formatFileSize(bytes) {
    if (!bytes) return '0 KB';
    const unitList = ['B', 'KB', 'MB', 'GB'];
    let value = bytes, i = 0;
    while (value >= 1024 && i < unitList.length - 1) { value /= 1024; i++; }
    return `${value.toLocaleString(Lang.locale, { maximumFractionDigits: 1 })} ${unitList[i]}`;
}

function formatDuration(minutes) {
    if (!minutes) return '—';
    const h = Math.floor(minutes / 60), m = minutes % 60;
    return h > 0
        ? (m > 0 ? t('shell.gioPhut', '{hour}h {minute}p', { hour: h, minute: m })
                 : t('shell.gio', '{hour}h', { hour: h }))
        : t('shell.phut', '{minute} phút', { minute: m });
}

/**
 * Nhóm danh sách theo tuần lịch (Thứ 2 -> Chủ nhật) dựa trên ngày trả về bởi getDate(item).
 * Trả về mảng nhóm { key, monday, sunday, isCurrent, itemList } sắp theo tuần tăng dần;
 * các mục không có ngày được gộp vào 1 nhóm cuối { key: null, itemList } giữ nguyên thứ tự gốc.
 */
function groupByWeek(list, getDate) {
    const pad = (n) => String(n).padStart(2, '0');
    const dateKey = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

    const scheduledList = [];
    const unscheduledList = [];
    for (const item of list) {
        const raw = getDate(item);
        const d = raw ? new Date(raw) : null;
        if (!d || isNaN(d)) { unscheduledList.push(item); continue; }

        const dow = (d.getDay() + 6) % 7; // Thứ 2 = 0 ... Chủ nhật = 6
        const monday = new Date(d.getFullYear(), d.getMonth(), d.getDate() - dow);
        const sunday = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 6);
        scheduledList.push({ item, key: dateKey(monday), monday, sunday });
    }
    scheduledList.sort((a, b) => a.key.localeCompare(b.key));

    const groupList = [];
    const groupByKey = new Map();
    for (const row of scheduledList) {
        let group = groupByKey.get(row.key);
        if (!group) {
            group = { key: row.key, monday: row.monday, sunday: row.sunday, itemList: [] };
            groupByKey.set(row.key, group);
            groupList.push(group);
        }
        group.itemList.push(row.item);
    }

    const today = new Date();
    const todayKey = dateKey(today);
    groupList.forEach(g => { g.isCurrent = todayKey >= g.key && todayKey <= dateKey(g.sunday); });

    if (unscheduledList.length) groupList.push({ key: null, itemList: unscheduledList });
    return groupList;
}

/** VD: "10/08 - 16/08". */
function formatWeekRange(monday, sunday) {
    const fmt = (d) => d.toLocaleDateString(Lang.locale, { day: '2-digit', month: '2-digit' });
    return `${fmt(monday)} - ${fmt(sunday)}`;
}

/**
 * Đầu một nhóm tuần trong danh sách lộ trình: biểu tượng, nhãn "Tuần dd/mm – dd/mm" (hoặc "Chưa xếp lịch")
 * và huy hiệu "Tuần này". Dùng chung cho cây lộ trình bên học viên lẫn bảng lộ trình bên quản trị — hai nơi
 * từng có hai bộ class riêng cho đúng một khối này, nên sửa cách hiển thị tuần phải nhớ sửa cả hai.
 *
 * @param {object} group Một nhóm do groupByWeek() trả về: { key, monday, sunday, isCurrent }.
 */
function weekHeadHtml(group) {
    const label = group.key
        ? `${t('common.tuan', 'Tuần')} ${formatWeekRange(group.monday, group.sunday)}`
        : t('course-detail.chuaXepLich', 'Chưa xếp lịch');

    return `<div class="week-head ${group.isCurrent ? 'current' : ''}">
        <span class="week-head-icon"><i class="ph ${group.key ? 'ph-calendar' : 'ph-push-pin'}"></i></span>
        <span class="week-head-label">${esc(label)}</span>
        ${group.isCurrent ? `<span class="week-head-now">${t('course-detail.tuanNay', 'Tuần này')}</span>` : ''}
    </div>`;
}

const initial = (name) => (name || '?').trim().split(/\s+/).pop().charAt(0).toUpperCase();

/** Nhãn màu cho trạng thái phân công / khóa học / bài học. */
function statusBadge(status) {
    const map = {
        Assigned:   ['gray',   t('dashboard.notStarted', 'Chưa bắt đầu')],
        NotStarted: ['gray',   t('dashboard.notStarted', 'Chưa bắt đầu')],
        InProgress: ['blue',   t('dashboard.inProgress', 'Đang học')],
        Completed:  ['green',  t('index.stat-enroll', 'Hoàn thành')],
        Failed:     ['red',    t('shell.khongDat', 'Không đạt')],
        Cancelled:  ['gray',   t('online-class.daHuy', 'Đã hủy')],
        Draft:                   ['orange', t('status.Draft', 'Bản nháp')],
        Rejected:                ['red',    t('course.tuChoi', 'Từ chối')],
        PendingHeadApproval:     ['cyan',   t('course.choTruongPhongDuyet', 'Chờ Trưởng phòng duyệt')],
        PendingDirectorApproval: ['cyan',   t('course.choGiamDocDuyet', 'Chờ Giám Đốc duyệt')],
        PendingHrApproval:       ['cyan',   t('course.choGiamDocNhanSuDuyet', 'Chờ Giám Đốc Nhân sự duyệt')],
        // Khóa học chạy theo luồng cấp duyệt do Admin định nghĩa — tên bước đang chờ nằm trên thanh tiến trình
        // ngay dưới dòng, nên nhãn trạng thái ở đây không cần lặp lại cấp nào đang giữ.
        PendingApproval:         ['cyan',   t('course.choDuyet', 'Chờ duyệt')],
        Published:  ['green',  t('status.Published', 'Đã xuất bản')],
        Archived:   ['gray',   t('status.Archived', 'Lưu trữ')],
        Scheduled:  ['gray',   t('online-class.daLenLich', 'Đã lên lịch')],
        Live:       ['red',    t('online-class.dangDienRa', 'Đang diễn ra')],
        Ended:      ['gray',   t('online-class.daKetThuc', 'Đã kết thúc')]
    };
    const [color, label] = map[status] || ['gray', status || '—'];
    return `<span class="badge ${color}">${esc(label)}</span>`;
}

// ==================== CHUỖI DUYỆT XUẤT BẢN KHÓA HỌC ====================
// Trưởng Nhóm tạo -> gửi Trưởng phòng duyệt -> Giám Đốc phòng ban duyệt -> Giám Đốc Nhân sự duyệt & xuất bản.

/**
 * Người đang đăng nhập có được Duyệt / Từ chối khóa học này ở trạng thái hiện tại không.
 *
 * Tách riêng khỏi courseWorkflowActionsHtml vì trang Tổng quan cần đúng câu hỏi này để dựng khối "Khóa học
 * chờ bạn duyệt" mà không cần tới HTML của mấy cái nút. Chép lại luật sang trang kia thì sớm muộn hai bên
 * lệch nhau: dashboard kê ra khóa học mà mở vào lại không có nút duyệt nào, hoặc ngược lại.
 *
 * Xét qua Auth.hasRole chứ không so bằng Auth.role: vai trò kế thừa mang tên riêng ("Giám Đốc IT") nhưng vẫn
 * là Giám Đốc. So bằng thì API cho duyệt mà nút Duyệt không bao giờ hiện ra — người dùng không có cách nào bấm.
 */
function canApproveCourse(course, hrDepartmentId) {
    const myDepartmentId = Auth.profile?.departmentId;
    const sameDepartment = myDepartmentId != null && myDepartmentId === course.createdByDepartmentId;
    const isHrDirector = Auth.hasRole('Giám Đốc') && myDepartmentId != null && myDepartmentId === hrDepartmentId;

    switch (course.status) {
        // Khóa học chạy theo LUỒNG CẤP DUYỆT do Admin định nghĩa: server đã tính sẵn course.canApprove vì chỉ
        // server mới biết bước đang chờ giao cho vai trò nào ở phòng ban nào. Giao diện không đoán lại theo vai
        // trò như ba nhánh dưới — số bước và cấp duyệt của mỗi luồng mỗi khác, đoán là sai.
        case 'PendingApproval': return !!course.canApprove;
        case 'PendingHeadApproval': return Auth.hasRole('Trưởng phòng') && sameDepartment;
        case 'PendingDirectorApproval': return Auth.hasRole('Giám Đốc') && sameDepartment;
        case 'PendingHrApproval': return isHrDirector || Auth.isApprovalDelegate;
        default: return false;
    }
}

/**
 * Nút thao tác phù hợp với vai trò + phòng ban của người đang đăng nhập cho 1 khóa học.
 * hrDepartmentId: id phòng ban có mã "HR", dùng để phân biệt Giám Đốc phòng ban thường với Giám Đốc Nhân sự.
 */
function courseWorkflowActionsHtml(course, hrDepartmentId) {
    // Chỉ chính Trưởng Nhóm hoặc Giảng Viên đã soạn khóa học mới gửi duyệt được. Admin cố ý không có nút này: Admin không
    // thuộc phòng ban nào nên nội dung do Admin gửi lên sẽ không tới được Trưởng phòng nào để duyệt.
    if ((course.status === 'Draft' || course.status === 'Rejected') && course.createdById === Auth.profile?.id) {
        if (Auth.isInstructor)
            return `<button data-i18n="shell.xuatBan" class="btn sm" data-submit="${course.id}">Xuất bản</button>`;
        if (Auth.isTrainer)
            return `<button data-i18n="shell.guiDuyet" class="btn sm" data-submit="${course.id}">Gửi duyệt</button>`;
    }

    // Bước đang chờ do một người được CHỈ ĐỊNH đích danh duyệt, mà người đó nghỉ phép / nghỉ việc / chọn nhầm
    // thì khóa học nằm chờ mãi. Server đã tính sẵn cờ này (chỉ Admin và chính người soạn mới đổi được, và chỉ
    // với bước thuộc loại phải chỉ định) — giao diện không đoán lại.
    const reassignHtml = course.canReassignApprover
        ? `<button class="btn ghost sm" data-reassign="${course.id}" data-reassign-step="${course.currentStepOrder}"
                   title="${esc(t('course.doiNguoiDuyetHint', 'Chuyển bước đang chờ cho người khác duyệt'))}"
           >${esc(t('course.doiNguoiDuyet', 'Đổi người duyệt'))}</button>`
        : '';

    if (canApproveCourse(course, hrDepartmentId)) {
        // Bước cuối thì duyệt xong là khóa học xuất bản luôn — nói rõ trên nút, vì hai việc đó khác hẳn nhau
        // về hậu quả: bước giữa còn cấp sau soát lại, bước cuối là học viên thấy ngay.
        const isLastWorkflowStep = course.status === 'PendingApproval'
            && course.currentStepOrder >= (course.approvalStepNameList?.length || 0);
        const isPublishingStep = isLastWorkflowStep || course.status === 'PendingHrApproval';

        return reassignHtml + courseApproveRejectHtml(course.id,
            isPublishingStep ? t('shell.duyetXuatBan', 'Duyệt & Xuất bản') : t('shell.duyet', 'Duyệt'));
    }

    if (reassignHtml) return reassignHtml;

    if (course.status === 'Published' && Auth.isAdmin)
        return `<button class="btn ghost sm" data-archive="${course.id}"><i class="ph ph-archive"></i> <span data-i18n="status.Archived">Lưu trữ</span></button>`;

    // Lưu trữ phải có đường quay lại: không có nút này thì một cú bấm nhầm khóa cứng khóa học vĩnh viễn —
    // mọi nút sửa nội dung đều bị ẩn ở trạng thái Archived và không còn hành động nào khác trên giao diện.
    // Đưa về Bản nháp (không phải Published) để nội dung đi lại đúng chuỗi duyệt trước khi xuất bản lần nữa.
    if (course.status === 'Archived' && Auth.isAdmin)
        return `<button class="btn ghost sm" data-unarchive="${course.id}"><i class="ph ph-arrow-counter-clockwise"></i> <span data-i18n="shell.boLuuTru">Bỏ lưu trữ</span></button>`;

    return '';
}

/** Duyệt là nút chính (đặc), Từ chối là nút viền chữ đỏ: hai nút đặc cạnh nhau thì mắt không biết nên bấm cái
 *  nào, mà Từ chối lại là thao tác bắt người soạn làm lại — không được nổi bật ngang hàng Duyệt. */
function courseApproveRejectHtml(id, approveLabel) {
    return `<button class="btn sm" data-approve="${id}"><i class="ph-bold ph-check"></i> ${esc(approveLabel)}</button>
            <button class="btn ghost danger sm" data-reject="${id}"><i class="ph-bold ph-x"></i> <span data-i18n="course.tuChoi">Từ chối</span></button>`;
}

/** Màu nền ảnh bìa suy ra từ tên khóa học — khóa nào chưa có ảnh vẫn có một mảng màu riêng, ổn định qua các
 *  lần tải (không random), nên người dùng nhận ra khóa học quen thuộc bằng màu. Dùng chung cho Tổng quan,
 *  Khóa học của tôi và Quản lý khóa học: cùng một khóa học phải mang cùng một màu ở mọi trang. */
function courseThumbBackground(title) {
    const paletteList = [
        'linear-gradient(135deg,#00a1e6,#0369a1)', 'linear-gradient(135deg,#8b5cf6,#6d28d9)',
        'linear-gradient(135deg,#f43f5e,#be123c)', 'linear-gradient(135deg,#f59e0b,#c2410c)',
        'linear-gradient(135deg,#10b981,#047857)', 'linear-gradient(135deg,#06b6d4,#0e7490)',
    ];
    let hash = 0;
    for (const ch of String(title || '')) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
    return paletteList[hash % paletteList.length];
}

/** Ảnh bìa nhỏ của một khóa học: ảnh thật nếu có, không thì mảng màu theo tên + biểu tượng. */
function courseThumbHtml(title, thumbnailUrl, className = 'course-mini-thumb', icon = 'ph-book-open-text', brand = false) {
    // brand = true: khóa chưa có ảnh bìa hiện logo Hòa Bình trên nền trắng thay cho mảng màu + biểu tượng.
    if (brand && !thumbnailUrl) {
        return `<div class="${className} is-brand" aria-hidden="true"><img class="brand-logo" src="/img/HoaBinh.png" alt="" loading="lazy"></div>`;
    }
    return `<div class="${className}${thumbnailUrl ? ' has-cover' : ''}" style="--cover-fallback:${courseThumbBackground(title)}" aria-hidden="true">
        <i class="ph-duotone ${icon}"></i>
        ${thumbnailUrl ? courseCoverHtml(thumbnailUrl) : ''}
    </div>`;
}

/**
 * Ảnh bìa người soạn tải lên, hiện TRỌN VẸN (object-fit: contain) — cắt cho đầy khung (cover) thì logo, ảnh
 * có chữ bị xén mất mép, mà khung ảnh bìa ở mỗi trang một tỉ lệ nên không có cách cắt nào hợp cho mọi chỗ.
 * Phần khung thừa không để trống mà phủ bằng chính ảnh đó phóng to và làm mờ. Hai thẻ <img> cùng một địa chỉ
 * nên trình duyệt chỉ tải một lần. Ảnh hỏng thì gỡ cả hai, khung quay về mảng màu + biểu tượng.
 *
 * Khung chứa phải mang lớp has-cover, có position: relative và overflow: hidden (xem css/style.css).
 */
function courseCoverHtml(url) {
    return `<img class="cover-backdrop" src="${esc(url)}" alt="" aria-hidden="true" loading="lazy">
        <img class="cover-image" src="${esc(url)}" alt="" loading="lazy"
             onerror="const box = this.parentNode; box.classList.remove('has-cover'); box.querySelectorAll('.cover-backdrop, .cover-image').forEach(x => x.remove())">`;
}

/**
 * Điểm sao trung bình ở góc thẻ khóa học. Chưa ai đánh giá thì hiện chữ mờ "Chưa có đánh giá" chứ không hiện
 * "0 sao": thấp nhất một đánh giá là 1 sao, số 0 sẽ đọc thành "khóa này bị chấm tệ".
 * Chỗ đặt (góc dưới phải thẻ hàng, hay cuối phần thân thẻ lưới) do CSS .course-rating quyết định — xem css/style.css.
 */
function courseRatingHtml(average, count) {
    if (average == null || !count)
        return `<span class="course-rating none"><i class="ph ph-star"></i> ${esc(t('feedback.chuaCoDanhGiaNgan', 'Chưa có đánh giá'))}</span>`;
    const label = t('feedback.soSao', '{n} trên 5 sao', { n: Number(average).toFixed(1) });
    return `<span class="course-rating" title="${esc(label)} · ${esc(t('feedback.luotDanhGia', '{n} lượt đánh giá', { n: count }))}" aria-label="${esc(label)}">
        <i class="ph-fill ph-star"></i> ${Number(average).toFixed(1)} <small>(${esc(Number(count).toLocaleString(Lang.locale))})</small></span>`;
}

/** Các bước trong chuỗi duyệt nội dung.
 *  Phải là HÀM chứ không phải hằng: t() gọi ở cấp module sẽ chạy TRƯỚC khi /locales/*.json
 *  tải xong, nên nhãn sẽ mãi mãi là tiếng Việt dù đang chọn tiếng Anh.
 *  roleLabel() chỉ dịch phần HIỂN THỊ — chuỗi so sánh phân quyền vẫn giữ nguyên. */
function approvalStepList() {
    return [
        { status: 'Draft', label: t('shell.soan', 'Soạn') },
        { status: 'PendingHeadApproval', label: roleLabel('Trưởng phòng') },
        { status: 'PendingDirectorApproval', label: roleLabel('Giám Đốc') },
        { status: 'PendingHrApproval', label: t('shell.gdNhanSu', 'GĐ Nhân sự') },
        { status: 'Published', label: t('shell.xuatBan', 'Xuất bản') }
    ];
}

/**
 * Lộ trình duyệt hiển thị ngay dưới mỗi khóa học trong danh sách. Số bước không cố định: khóa học có
 * chọn LUỒNG CẤP DUYỆT thì vẽ theo đúng các bước của luồng đó, không chọn thì vẽ chuỗi cố định 5 bước mô tả bên dưới.
 *
 * Lộ trình duyệt cố định (5 bước), cho biết
 * bài đang ở bước nào — tô xanh các bước đã qua, tô bước hiện tại, tô đỏ bước bị từ chối (nếu có).
 * Khi bị từ chối, xác định đúng bước 1/2/3 theo rejectedByRoleName — backend luôn lưu tên bước chuẩn
 * ("Trưởng phòng"/"Giám Đốc"/"Giám Đốc Nhân sự") ở đây, kể cả khi người được ủy quyền xử lý bước cuối.
 * Nhãn bước cuối hiển thị theo course.hrApproverLabel — chỉ chính Giám Đốc Nhân sự mới nhận về nhãn kèm tên
 * người ("Giám Đốc Nhân sự · Nguyễn Văn A"): tên người đang được ủy quyền khi bước cuối chưa xử lý, tên người
 * đã duyệt/từ chối thay khi đã xử lý. Các vai trò khác luôn nhận về "Giám Đốc Nhân sự" (hoặc null = giữ nhãn
 * mặc định của bước), dù thực tế ai đã xử lý (xem CourseController.ResolveHrApproverLabel).
 */
const HR_STEP_INDEX = 3;

function approvalTrackHtml(course, workflowOptionList = []) {
    if (course.status === 'Archived' || Auth.isInstructor) return '';

    const isRejected = course.status === 'Rejected';
    const isPublished = course.status === 'Published';

    // Hai nguồn bước khác nhau, cùng một cách vẽ: khóa học có chọn LUỒNG CẤP DUYỆT thì lấy đúng các bước
    // Admin đã định nghĩa (server trả kèm trong approvalStepNameList), kẹp giữa "Soạn" và "Xuất bản"; không chọn
    // luồng thì vẫn là chuỗi cố định 5 bước như cũ. Bước đang đứng của luồng đọc thẳng từ currentStepOrder
    // (đánh số từ 1, trùng đúng chỉ số sau khi chèn "Soạn" vào đầu danh sách).
    const useWorkflow = !!course.approvalWorkflowId && !!course.approvalStepNameList?.length;
    let labelList, currentIndex;

    if (useWorkflow) {
        labelList = [t('shell.soan', 'Soạn'), ...course.approvalStepNameList, t('shell.xuatBan', 'Xuất bản')];
        currentIndex = isPublished ? labelList.length - 1
            : course.status === 'Draft' ? 0
            : (course.currentStepOrder || 0);
        // Bước đang chờ đã chỉ định đích danh người duyệt thì ghi luôn tên họ lên thanh tiến trình — đó là
        // câu hỏi đầu tiên của người soạn khi nhìn vào ("đang nằm ở ai"), mà tên bước không trả lời được.
        // Server chỉ trả tên này khi khóa học đang chờ duyệt và bước đó thuộc loại chỉ định đích danh.
        if (course.currentStepApproverName && labelList[currentIndex] !== undefined)
            labelList[currentIndex] = `${labelList[currentIndex]} · ${course.currentStepApproverName}`;
    } else {
        const stepList = approvalStepList();
        labelList = stepList.map((step, i) =>
            (i === HR_STEP_INDEX && course.hrApproverLabel) ? course.hrApproverLabel : step.label);
        const REJECT_STEP_INDEX = { 'Trưởng phòng': 1, 'Giám Đốc': 2, 'Giám Đốc Nhân sự': HR_STEP_INDEX };
        currentIndex = isRejected
            ? (REJECT_STEP_INDEX[course.rejectedByRoleName] ?? 2)
            : stepList.findIndex(s => s.status === course.status);
    }

    const stepHtml = labelList.map((stepLabel, i) => {
        let cls, label = stepLabel, title = '';
        if (isRejected && i === currentIndex) {
            cls = 'rejected';
            label = `${label} · Từ chối`;
            if (course.rejectionReason) title = ` title="${esc(course.rejectionReason)}"`;
        } else if (isPublished || i < currentIndex) {
            cls = 'done';
        } else if (!isRejected && i === currentIndex) {
            cls = 'current';
        } else {
            cls = 'pending';
        }

        const lineDone = isPublished || i <= currentIndex;
        const line = i > 0 ? `<span class="track-line ${lineDone ? (isRejected && i === currentIndex ? 'rejected' : 'done') : ''}"></span>` : '';
        return `${line}<span class="track-step ${cls}"${title}><span class="track-dot"></span>${esc(label)}</span>`;
    }).join('');

    // Ô chọn luồng đặt ngay đầu thanh tiến trình — đây mới là chỗ người soạn đang nhìn khi họ tự hỏi
    // "bài này sẽ đi qua những ai", nên để họ đổi luồng ngay tại đó thay vì phải mở biểu mẫu Sửa.
    return `<div class="approval-track-line">${approvalWorkflowPickerHtml(course, workflowOptionList)}
                <div class="approval-track">${stepHtml}</div>
            </div>`;
}

/**
 * Ô chọn luồng cấp duyệt hiện ngay trước thanh tiến trình của mỗi khóa học.
 *
 * Chỉ chính người soạn mới đổi được, và chỉ khi khóa học còn ở đầu chuỗi (Nháp / Từ chối) — đúng ràng buộc
 * mà server đang giữ ở ChangeApprovalWorkflow, hiện nút rồi mới báo lỗi thì chỉ tốn công người dùng. Những
 * người còn lại (các cấp duyệt) chỉ thấy TÊN luồng dạng chữ: thanh tiến trình chỉ hiện tên các bước, mà hai
 * luồng khác nhau hoàn toàn có thể đặt tên bước giống hệt nhau.
 */
function approvalWorkflowPickerHtml(course, workflowOptionList = []) {
    const isOwnEditableCourse = !Auth.isAdmin && Auth.isTrainer && course.createdById === Auth.profile?.id
        && (course.status === 'Draft' || course.status === 'Rejected') && !Auth.isInstructor;

    if (!isOwnEditableCourse || !workflowOptionList.length)
        return course.approvalWorkflowName
            ? `<span class="track-workflow static"><i class="ph ph-git-branch"></i>${esc(course.approvalWorkflowName)}</span>`
            : '';

    // Luồng đang gắn có thể đã bị Admin cho ngừng hoạt động nên không còn trong danh sách chọn. Vẫn phải chèn
    // nó vào, không thì ô chọn hiển thị "chuỗi duyệt mặc định" và trông như khóa học chưa từng chọn luồng nào.
    const optionList = [...workflowOptionList];
    if (course.approvalWorkflowId && !optionList.some(x => x.id === course.approvalWorkflowId))
        optionList.push({ id: course.approvalWorkflowId, name: course.approvalWorkflowName || '—' });

    return `<span class="track-workflow">
        <select class="track-workflow-select" data-workflow-course="${course.id}"
                aria-label="${esc(t('course.luongCapDuyet', 'Luồng cấp duyệt'))}">
            <option value="">${esc(t('course.chuoiDuyetMacDinh', 'Chuỗi duyệt mặc định'))}</option>
            ${optionList.map(w => `<option value="${w.id}"${w.id === course.approvalWorkflowId ? ' selected' : ''}>${esc(w.name)}</option>`).join('')}
        </select></span>`;
}

/** Gắn sự kiện cho các nút do courseWorkflowActionsHtml() sinh ra, trong phạm vi container. */
function bindCourseWorkflowButtons(container, onDone) {
    // Ô chọn luồng cấp duyệt trên thanh tiến trình (xem approvalWorkflowPickerHtml). Dùng action riêng chứ không
    // phải PUT /course/{id}: biểu mẫu đầy đủ đòi tên, thời lượng, điểm đạt... mà ở đây chỉ có mỗi id khóa học.
    container.querySelectorAll('[data-workflow-course]').forEach(select => {
        enhanceSearchableSelect(select, { placeholder: t('course.goTenLuong', 'Gõ tên luồng...') });
        select.addEventListener('change', async () => {
            try {
                const r = await API.patch(`/course/${select.dataset.workflowCourse}/approval-workflow?workflowId=${select.value}`);
                toastOk(r.message);
            } catch (ex) { toastErr(ex.message); }
            // Tải lại dù thành công hay không: thành công thì thanh tiến trình phải vẽ theo luồng mới, thất bại thì
            // ô chọn phải quay về đúng luồng thật — để nguyên là giao diện nói một đằng, dữ liệu một nẻo.
            onDone();
        });
    });

    container.querySelectorAll('[data-submit]').forEach(b => b.addEventListener('click', async () => {
        const courseId = b.dataset.submit;
        try {
            // Không hỏi người duyệt nữa: người duyệt từng bước do người dựng luồng ghim sẵn trong định nghĩa
            // luồng, người soạn chỉ chọn luồng trên biểu mẫu khóa học.
            const r = await API.post(`/course/${courseId}/submit`);
            toastOk(r.message);
            onDone();
        }
        catch (ex) { toastErr(ex.message); }
    }));
    container.querySelectorAll('[data-reassign]').forEach(b => b.addEventListener('click', async () => {
        const courseId = b.dataset.reassign;
        const stepOrder = +b.dataset.reassignStep;
        try {
            const optionList = await API.get(`/course/${courseId}/step-approver-option`);
            const picked = await pickApproverDialog(optionList, {
                stepOrder,
                title: t('course.doiNguoiDuyet', 'Đổi người duyệt'),
                message: t('course.chonNguoiDuyetMoi',
                    'Chọn người duyệt mới cho bước đang chờ. Người được chọn sẽ nhận thông báo ngay, người cũ thôi giữ bước này.'),
                okText: t('common.save', 'Lưu')
            });
            if (picked === null) return; // đã hủy

            const r = await API.post(`/course/${courseId}/reassign-approver`, picked[0]);
            toastOk(r.message);
            onDone();
        }
        catch (ex) { toastErr(ex.message); }
    }));
    container.querySelectorAll('[data-approve]').forEach(b => b.addEventListener('click', async () => {
        try { const r = await API.post(`/course/${b.dataset.approve}/approve`); toastOk(r.message); onDone(); }
        catch (ex) { toastErr(ex.message); }
    }));
    container.querySelectorAll('[data-reject]').forEach(b => b.addEventListener('click', async () => {
        const reason = await promptDialog(t('shell.vuiLongNhapLyDoTuChoiNoiDungNa', 'Vui lòng nhập lý do từ chối nội dung này. Khóa học sẽ chuyển sang trạng thái Từ chối để chỉnh sửa lại.'),
            { title: t('shell.tuChoiNoiDung', 'Từ chối nội dung'), okText: t('course.tuChoi', 'Từ chối'), danger: true, placeholder: t('shell.lyDoTuChoi', 'Lý do từ chối *') });
        if (reason === null) return; // đã hủy
        try { const r = await API.post(`/course/${b.dataset.reject}/reject`, { reason }); toastOk(r.message); onDone(); }
        catch (ex) { toastErr(ex.message); }
    }));
    container.querySelectorAll('[data-archive]').forEach(b => b.addEventListener('click', async () => {
        try { const r = await API.patch(`/course/${b.dataset.archive}/status?status=2`); toastOk(r.message); onDone(); }
        catch (ex) { toastErr(ex.message); }
    }));
    container.querySelectorAll('[data-unarchive]').forEach(b => b.addEventListener('click', async () => {
        if (!await confirmDialog(t('shell.duaKhoaHocTuLuuTruVeBanNhapDeC', 'Đưa khóa học từ Lưu trữ về Bản nháp để chỉnh sửa lại? ')
            + t('shell.khoaHocSePhaiDiLaiChuoiDuyetTr', 'Khóa học sẽ phải đi lại chuỗi duyệt trước khi xuất bản cho học viên.'), { okText: t('shell.boLuuTru', 'Bỏ lưu trữ') })) return;
        try { const r = await API.patch(`/course/${b.dataset.unarchive}/status?status=0`); toastOk(r.message); onDone(); }
        catch (ex) { toastErr(ex.message); }
    }));
}

/**
 * Thanh tiến độ dùng chung. Trước đây mỗi trang tự vẽ một bản (thẻ khóa học của tôi, bảng báo cáo), nên
 * cùng một con số phần trăm hiện ra ba chiều cao và ba màu "đã xong" khác nhau.
 *
 * @param {number}  percent
 * @param {object}  [options]
 * @param {string}  [options.label] Có nhãn thì % nhảy lên đứng cùng nhãn ở dòng trên, thanh chiếm trọn bề
 *                                  ngang bên dưới — dạng dùng cho thẻ khóa học. Không nhãn thì % đứng cạnh
 *                                  thanh trên cùng một dòng — dạng dùng trong ô của bảng.
 * @param {boolean} [options.large] Thanh dày hơn, cho những chỗ tiến độ là thông tin chính chứ không phải
 *                                  một cột phụ trong bảng.
 */
/**
 * Gắn hành vi cho một khung kéo thả tệp (.drop-zone trong css/style.css): bấm để mở hộp chọn tệp, kéo tệp
 * qua thì sáng lên, thả hoặc chọn xong thì gọi onPick.
 *
 * Cú thả rỗng (kéo một đoạn chữ, một đường dẫn, hoặc thả hụt ra ngoài) KHÔNG gọi onPick: một trong hai
 * trang từng gọi thẳng onPick(files[0]) nên thả hụt là xóa mất tệp người dùng vừa chọn xong.
 *
 * @param {HTMLElement}      zone   Phần tử .drop-zone.
 * @param {HTMLInputElement} input  Ô <input type="file"> ẩn đi kèm.
 * @param {Function}         onPick (File) => void, chỉ gọi khi thật sự có tệp.
 */
function attachDropZone(zone, input, onPick) {
    if (!zone || !input) return;

    zone.addEventListener('click', () => input.click());
    zone.addEventListener('dragover', (e) => { e.preventDefault(); zone.classList.add('dragging'); });
    zone.addEventListener('dragleave', () => zone.classList.remove('dragging'));
    zone.addEventListener('drop', (e) => {
        e.preventDefault();
        zone.classList.remove('dragging');
        const file = e.dataTransfer?.files?.[0];
        if (file) onPick(file);
    });
    input.addEventListener('change', (e) => { if (e.target.files[0]) onPick(e.target.files[0]); });
}

function progressBar(percent, options = {}) {
    const { label = '', large = false } = options;
    const value = Math.round(percent || 0);
    const bar = `<div class="progress ${large ? 'lg' : ''} ${value >= 100 ? 'done' : ''}">
        <span style="width:${value}%"></span>
    </div>`;

    if (label) return `<div class="progress-block">
        <div class="progress-cap"><span>${label}</span><span>${value}%</span></div>
        ${bar}
    </div>`;

    return `<div class="flex gap-sm">${bar}<small class="muted nowrap">${value}%</small></div>`;
}

// ==================== BIỂU TƯỢNG & NHÃN CỦA NỘI DUNG ====================
// Mỗi loại nội dung chỉ được có MỘT biểu tượng trong toàn hệ thống. Trước đây mỗi trang giữ một bảng tra
// riêng, nên cùng một bài video hiện ph-video-camera ở trang quản lý nội dung nhưng ph-play-circle ở màn
// hình học — người dùng không có cách nào biết đó là cùng một thứ.

/** Biểu tượng + nhãn của một dạng nội dung bài học. Bộ biểu tượng chọn theo NGHĨA chứ không theo tên loại:
 *  bài lý thuyết là một bài đọc (article), tài liệu là tệp đính kèm (paperclip), video là thứ bấm để phát
 *  (play-circle) — nhìn biểu tượng là đoán được sẽ nhận được gì khi mở. */
function contentTypeMeta(type) {
    const META = {
        Text:     { iconClass: 'ph-article',     label: t('common.lyThuyet', 'Lý thuyết') },
        Document: { iconClass: 'ph-paperclip',   label: t('common.taiLieu', 'Tài liệu') },
        Video:    { iconClass: 'ph-play-circle', label: t('common.video', 'Video') },
        Image:    { iconClass: 'ph-image',       label: t('common.hinhAnh', 'Hình ảnh') },
        Link:     { iconClass: 'ph-link',        label: t('common.lienKet', 'Liên kết') },
    };
    const meta = META[type] || META.Text;
    return { ...meta, icon: `<i class="ph ${meta.iconClass}"></i>` };
}

/** Chỉ lấy thẻ <i> của một dạng nội dung — lối tắt cho những chỗ chỉ cần biểu tượng, không cần nhãn. */
const contentTypeIcon = (type) => contentTypeMeta(type).icon;

/** Biểu tượng + nhãn của một BƯỚC trong lộ trình (bài học / bài kiểm tra / mô phỏng 3D).
 *  `mark` là màu ô số thứ tự trong cây lộ trình bên phía quản trị (xem .pm-mark trong css/style.css);
 *  phía học viên không dùng tới, nhưng để chung một chỗ thì hai bên không thể lệch nhau về sau. */
function stepTypeMeta(itemType) {
    const META = {
        Lesson:     { iconClass: 'ph-file-text', mark: 'lesson', label: t('common.baiHoc', 'Bài học') },
        Quiz:       { iconClass: 'ph-exam',      mark: 'quiz',   label: t('common.baiKiemTra', 'Bài kiểm tra') },
        Simulation: { iconClass: 'ph-cube',      mark: 'quiz',   label: t('common.moPhong3d', 'Mô phỏng 3D') },
        Scorm:      { iconClass: 'ph-package',   mark: 'lesson', label: t('common.goiScorm', 'Gói SCORM') },
    };
    const meta = META[itemType] || META.Lesson;
    return { ...meta, icon: `<i class="ph ${meta.iconClass}"></i>` };
}

/**
 * Ô "Kết quả — Đạt / Chưa đạt" dùng chung cho mọi màn hình có chấm điểm (bài kiểm tra, mô phỏng 3D, phần
 * tóm tắt trong lộ trình học). Bốn nơi từng chép tay khối này và đã trôi mỗi nơi một kiểu: chỗ viết "ĐẠT"
 * chỗ viết "Đạt", cỡ chữ 19px lẫn 22px, biểu tượng "chưa đạt" lúc là đồng hồ cát lúc là dấu X.
 *
 * @param {boolean} isPassed
 * @param {object}  [options]
 * @param {boolean} [options.pending] Chưa làm bài lần nào (khác hẳn "đã làm mà trượt"): dùng đồng hồ cát và
 *                                    KHÔNG tô đỏ — tô đỏ một bài học viên còn chưa mở là báo hỏng oan.
 * @param {string}  [options.foot]    Dòng chú thích nhỏ dưới ô, VD "Điểm đạt tối thiểu: 70".
 */
function resultTile(isPassed, options = {}) {
    const { pending = false, foot = '' } = options;
    const tone = isPassed ? 'ok' : pending ? '' : 'bad';
    const icon = isPassed ? 'ph-check-circle' : pending ? 'ph-hourglass' : 'ph-x-circle';
    const text = isPassed ? t('common.dat', 'Đạt') : t('common.chuaDat', 'Chưa đạt');

    return `<div class="stat ${tone}">
        <div class="label">${t('common.ketQua', 'Kết quả')}</div>
        <div class="value result"><i class="ph ${icon}"></i> ${text}</div>
        ${foot ? `<div class="foot">${foot}</div>` : ''}
    </div>`;
}

// ==================== Ô NHẬP MẬT KHẨU ====================
// Thanh đo độ mạnh và nút con mắt nằm trong js/password.js — trang đăng nhập cần đúng widget đó nhưng cố ý
// không nạp app.js, nên để ở đây thì nó buộc phải giữ một bản chép tay và hai bản sẽ trôi khỏi nhau.

// ==================== HỘP THOẠI ====================
function openModal(id) {
    const box = document.getElementById(id);
    if (!box) return;
    resetPasswordEye(box);
    box.classList.add('open');
}
function closeModal(id) { document.getElementById(id)?.classList.remove('open'); }

// Chỉ đóng modal khi cả lúc nhấn chuột xuống lẫn lúc thả chuột đều nằm trên lớp nền — tránh trường hợp
// bôi đen (kéo chọn) văn bản bên trong modal rồi thả chuột hơi lệch ra ngoài mép modal bị tính nhầm thành
// "click" trên lớp nền, khiến modal tự tắt ngay giữa lúc đang chọn chữ.
// Lớp nền thường là chính .modal-mask, nhưng một hộp thoại có thể trải phần tử riêng lên khoảng trống đó
// (bảng soạn tình huống 3D đặt cảnh mô phỏng ra ngoài khung hộp thoại) — phần tử ấy gắn data-backdrop để
// khoảng trống quanh nó vẫn đóng được hộp thoại như cũ.
const backdropMaskOf = (el) => el?.classList?.contains('modal-mask') ? el
    : (el?.hasAttribute?.('data-backdrop') ? el.closest('.modal-mask') : null);
let maskMouseDownOnBackdrop = false;
document.addEventListener('mousedown', (e) => {
    maskMouseDownOnBackdrop = !!backdropMaskOf(e.target);
});
document.addEventListener('click', (e) => {
    const mask = backdropMaskOf(e.target);
    if (maskMouseDownOnBackdrop && mask) mask.classList.remove('open');
});

// ==================== HỘP THOẠI XÁC NHẬN ====================
let confirmResolve = null;
let confirmMaskMouseDown = false;
/** Giá trị trả về khi đóng hộp thoại bằng × / bấm ra ngoài — xem confirmDialog(options.cancelText). */
let confirmDismissValue = false;

function ensureConfirmModal() {
    if (document.getElementById('confirm-modal')) return;

    document.body.insertAdjacentHTML('beforeend', `
        <div class="modal-mask" id="confirm-modal">
            <div class="modal" style="max-width:400px">
                <div class="modal-head">
                    <h3 data-i18n="fp.confirm" id="confirm-title">Xác nhận</h3>
                    <button class="close-x" id="confirm-close" type="button">×</button>
                </div>
                <div class="modal-body" id="confirm-message"></div>
                <div class="modal-foot">
                    <button data-i18n="common.cancel" class="btn ghost" id="confirm-cancel" type="button">Hủy</button>
                    <button data-i18n="shell.dongY" class="btn" id="confirm-ok" type="button">Đồng ý</button>
                </div>
            </div>
        </div>`);

    const settle = (result) => {
        closeModal('confirm-modal');
        confirmResolve?.(result);
        confirmResolve = null;
    };
    document.getElementById('confirm-ok').addEventListener('click', () => settle(true));
    document.getElementById('confirm-cancel').addEventListener('click', () => settle(false));
    document.getElementById('confirm-close').addEventListener('click', () => settle(confirmDismissValue));
    document.getElementById('confirm-modal').addEventListener('mousedown', (e) => {
        confirmMaskMouseDown = e.target.id === 'confirm-modal';
    });
    document.getElementById('confirm-modal').addEventListener('click', (e) => {
        if (confirmMaskMouseDown && e.target.id === 'confirm-modal') settle(confirmDismissValue);
    });
}

/**
 * Hộp thoại xác nhận tùy biến — thay cho confirm() mặc định của trình duyệt (xấu, không đồng bộ giao diện).
 * @param {string} message Nội dung câu hỏi.
 * @param {{title?: string, okText?: string, cancelText?: string, danger?: boolean}} [options]
 *   cancelText: biến nút Hủy thành một lựa chọn thật (VD "Không"). Khi đó đóng hộp thoại bằng × hoặc bấm ra
 *   ngoài trả về null, để phân biệt "chọn Không" với "chưa chọn gì".
 * @returns {Promise<boolean|null>} true nếu bấm Đồng ý, false nếu bấm Hủy / nút cancelText,
 *   null nếu đóng hộp thoại khi có cancelText.
 */
function confirmDialog(message, options = {}) {
    ensureConfirmModal();

    // Gỡ data-i18n khi có tiêu đề riêng, gắn lại khi không.
    // Tiêu đề mặc định của hộp thoại mang data-i18n="fp.confirm", nên mỗi lần applyTranslation() quét lại
    // DOM (đổi ngôn ngữ, hoặc bất kỳ trang nào vẽ lại rồi gọi quét) là tiêu đề riêng bị thay bằng "Xác
    // nhận" — câu hỏi cụ thể biến mất mà không có lỗi nào báo ra.
    const titleEl = document.getElementById('confirm-title');
    if (options.title) titleEl.removeAttribute('data-i18n');
    else titleEl.setAttribute('data-i18n', 'fp.confirm');

    titleEl.textContent = options.title || t('fp.confirm', 'Xác nhận');
    document.getElementById('confirm-message').textContent = message;

    // Hai nút cũng mang data-i18n như tiêu đề — gỡ khi có chữ riêng, không thì lượt dịch lại giao diện sẽ
    // đổi chúng về "Đồng ý" / "Hủy".
    const okBtn = document.getElementById('confirm-ok');
    if (options.okText) okBtn.removeAttribute('data-i18n');
    else okBtn.setAttribute('data-i18n', 'shell.dongY');
    okBtn.textContent = options.okText || t('shell.dongY', 'Đồng ý');
    okBtn.className = `btn ${options.danger ? 'danger' : ''}`;

    const cancelBtn = document.getElementById('confirm-cancel');
    if (options.cancelText) cancelBtn.removeAttribute('data-i18n');
    else cancelBtn.setAttribute('data-i18n', 'common.cancel');
    cancelBtn.textContent = options.cancelText || t('common.cancel', 'Hủy');
    confirmDismissValue = options.cancelText ? null : false;

    openModal('confirm-modal');
    return new Promise((resolve) => { confirmResolve = resolve; });
}

// ==================== TỰ ĐĂNG KÝ KHÓA HỌC GỢI Ý ====================
/** Nhãn "vì sao khóa này hợp với bạn" cho mục gợi ý — giá trị SuggestReason của /course/my-available. */
function suggestReasonText(reason) {
    return {
        DepartmentPosition: t('suggest.reasonDepartmentPosition', 'Dành cho phòng ban và vị trí của bạn'),
        Department: t('suggest.reasonDepartment', 'Dành cho phòng ban của bạn'),
        Position: t('suggest.reasonPosition', 'Dành cho vị trí công việc của bạn'),
        All: t('suggest.reasonAll', 'Dành cho toàn công ty'),
    }[reason] || '';
}

/** Hỏi lại rồi đăng ký khóa học gợi ý cho chính người đang đăng nhập (dùng chung cho Tổng quan và Khóa học
 *  của tôi). Trả về true khi đăng ký xong — trang gọi tự tải lại danh sách của mình. */
async function selfEnrollCourse(courseId, courseTitle) {
    const ok = await confirmDialog(
        t('suggest.confirmEnroll', 'Đăng ký khóa học "{title}"? Khóa học sẽ được thêm vào mục Khóa học của tôi và bạn có thể bắt đầu học ngay.', { title: courseTitle }),
        { title: t('suggest.enroll', 'Đăng ký học'), okText: t('suggest.enroll', 'Đăng ký học') });
    if (!ok) return false;
    try {
        const r = await API.post(`/enrollment/self-enroll/${courseId}`);
        toastOk(r.message);
        return true;
    } catch (ex) {
        toastErr(ex.message);
        return false;
    }
}

// ==================== HỘP THOẠI NHẬP LÝ DO (bắt buộc) ====================
let promptResolve = null;
let promptMaskMouseDown = false;

/** Ô nhập đang được dùng của hộp thoại nhập liệu — một dòng hay nhiều dòng, tùy lượt mở. */
function activePromptInput() {
    const line = document.getElementById('prompt-line');
    return line.classList.contains('hidden') ? document.getElementById('prompt-input') : line;
}

function ensurePromptModal() {
    if (document.getElementById('prompt-modal')) return;

    document.body.insertAdjacentHTML('beforeend', `
        <div class="modal-mask" id="prompt-modal">
            <div class="modal" style="max-width:440px">
                <div class="modal-head">
                    <h3 data-i18n="shell.nhapNoiDung" id="prompt-title">Nhập nội dung</h3>
                    <button class="close-x" id="prompt-close" type="button">×</button>
                </div>
                <div class="modal-body">
                    <div id="prompt-message" class="mb"></div>
                    <!-- Hai ô nhập, mỗi lượt mở chỉ hiện MỘT. Một cái tên nhóm nhét vào khung ba dòng thì
                         cả hộp thoại trông như đang đòi người ta viết một đoạn văn, mà Enter ở đó lại
                         xuống dòng thay vì xác nhận. Ngược lại, lý do từ chối duyệt thì phải là khung
                         nhiều dòng. Xem tham số singleLine của promptDialog. -->
                    <input id="prompt-line" type="text" maxlength="200" class="hidden" autocomplete="off"
                           aria-labelledby="prompt-message">
                    <textarea id="prompt-input" rows="3" maxlength="1000" aria-labelledby="prompt-message"></textarea>
                    <div data-i18n="shell.vuiLongNhapNoiDungBatBuoc" class="hint" id="prompt-error" style="color:#dc2626;display:none">Vui lòng nhập nội dung bắt buộc.</div>
                </div>
                <div class="modal-foot">
                    <button data-i18n="common.cancel" class="btn ghost" id="prompt-cancel" type="button">Hủy</button>
                    <button data-i18n="shell.dongY" class="btn" id="prompt-ok" type="button">Đồng ý</button>
                </div>
            </div>
        </div>`);

    const settle = (result) => {
        closeModal('prompt-modal');
        promptResolve?.(result);
        promptResolve = null;
    };
    const submit = () => {
        const value = activePromptInput().value.trim();
        if (!value) { document.getElementById('prompt-error').style.display = ''; return; }
        settle(value);
    };
    document.getElementById('prompt-ok').addEventListener('click', submit);

    // Enter xác nhận — nhưng CHỈ ở ô một dòng. Ở khung nhiều dòng thì Enter phải là xuống dòng, chặn mất
    // là không ai gõ nổi một lý do từ chối dài hai đoạn.
    document.getElementById('prompt-line').addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.isComposing) { e.preventDefault(); submit(); }
    });
    document.getElementById('prompt-cancel').addEventListener('click', () => settle(null));
    document.getElementById('prompt-close').addEventListener('click', () => settle(null));
    document.getElementById('prompt-modal').addEventListener('mousedown', (e) => {
        promptMaskMouseDown = e.target.id === 'prompt-modal';
    });
    document.getElementById('prompt-modal').addEventListener('click', (e) => {
        if (promptMaskMouseDown && e.target.id === 'prompt-modal') settle(null);
    });
}

/**
 * Hộp thoại nhập một đoạn văn bản bắt buộc (VD lý do từ chối duyệt) — thay cho prompt() mặc định của trình duyệt.
 * @param {string} message Câu hỏi/hướng dẫn hiển thị phía trên ô nhập.
 * @param {{title?: string, okText?: string, danger?: boolean, placeholder?: string, value?: string,
 *           singleLine?: boolean, maxLength?: number}} [options]
 *   value: chữ điền sẵn trong ô nhập — dùng khi SỬA một giá trị đang có (đổi tên...), để người dùng chỉnh
 *   vài chữ thay vì gõ lại từ đầu. Bỏ trống thì ô nhập trống như trước.
 *   singleLine: dùng ô MỘT dòng thay cho khung ba dòng, và Enter xác nhận luôn. Đặt cho những giá trị
 *   bản chất là một dòng (tên nhóm, tiêu đề...); mặc định vẫn là khung nhiều dòng như cũ.
 *   maxLength: trần số ký tự, đặt cho khớp với trần của cột bên server.
 * @returns {Promise<string|null>} Chuỗi đã nhập (đã trim), hoặc null nếu bấm Hủy.
 */
function promptDialog(message, options = {}) {
    ensurePromptModal();
    document.getElementById('prompt-title').textContent = options.title || t('shell.nhapNoiDung', 'Nhập nội dung');
    document.getElementById('prompt-message').textContent = message;

    // Hai ô dùng chung cho mọi lượt mở nên phải đặt lại ĐỦ trạng thái mỗi lần, kể cả về mặc định:
    // lượt trước đặt maxLength = 200 mà lượt sau không đặt gì thì nó im lặng giữ nguyên 200.
    const singleLine = options.singleLine === true;
    const line = document.getElementById('prompt-line');
    const area = document.getElementById('prompt-input');
    line.classList.toggle('hidden', !singleLine);
    area.classList.toggle('hidden', singleLine);

    const input = singleLine ? line : area;
    input.maxLength = options.maxLength || (singleLine ? 200 : 1000);
    input.value = options.value || '';
    input.placeholder = options.placeholder || '';
    document.getElementById('prompt-error').style.display = 'none';

    const okBtn = document.getElementById('prompt-ok');
    okBtn.textContent = options.okText || t('shell.dongY', 'Đồng ý');
    okBtn.className = `btn ${options.danger ? 'danger' : ''}`;

    openModal('prompt-modal');
    input.focus();
    // Bôi đen sẵn phần chữ điền trước: người dùng gõ đè được ngay nếu muốn thay hẳn, mà vẫn bấm mũi tên để
    // chỉnh vài chữ được nếu chỉ muốn sửa.
    if (input.value) input.select();
    return new Promise((resolve) => { promptResolve = resolve; });
}

// ==================== HỘP THOẠI ĐỔI NGƯỜI DUYỆT ====================
// Chỉ còn phục vụ việc ĐỔI người duyệt của bước đang chờ, và chỉ Admin dùng tới (server chặn ở
// GET /course/{id}/step-approver-option). Người soạn không chỉ định người duyệt cho bài của mình nữa —
// người duyệt từng bước do người dựng luồng ghim sẵn trong định nghĩa luồng.
let approverResolve = null;
let approverMaskMouseDown = false;

function ensureApproverModal() {
    if (document.getElementById('approver-modal')) return;

    document.body.insertAdjacentHTML('beforeend', `
        <div class="modal-mask" id="approver-modal">
            <div class="modal" style="max-width:560px">
                <div class="modal-head">
                    <h3 id="approver-title">Chỉ định người duyệt</h3>
                    <button class="close-x" id="approver-close" type="button">×</button>
                </div>
                <div class="modal-body">
                    <div id="approver-message" class="muted small mb"></div>
                    <div id="approver-step-list"></div>
                    <div class="hint" id="approver-error" style="color:#dc2626;display:none"></div>
                </div>
                <div class="modal-foot">
                    <button data-i18n="common.cancel" class="btn ghost" id="approver-cancel" type="button">Hủy</button>
                    <button class="btn" id="approver-ok" type="button">Đồng ý</button>
                </div>
            </div>
        </div>`);

    const settle = (result) => {
        closeModal('approver-modal');
        approverResolve?.(result);
        approverResolve = null;
    };

    document.getElementById('approver-ok').addEventListener('click', () => {
        const selectList = [...document.querySelectorAll('#approver-step-list [data-step-order]')];
        // Chỉ bước BẮT BUỘC chỉ định mới không được để trống; bước gửi cả nhóm để trống là hợp lệ, nghĩa là
        // không khoanh vào ai cả.
        if (selectList.some(x => x.dataset.required && !x.value)) {
            const error = document.getElementById('approver-error');
            error.textContent = t('course.vuiLongChonNguoiDuyetChoMoiBuoc',
                'Vui lòng chọn người duyệt cho mọi bước còn trống.');
            error.style.display = '';
            return;
        }
        // Gửi lên CẢ những bước để trống (userId = 0): server phân biệt "vừa bỏ chọn bước này" với "bước này
        // không có trong yêu cầu" — chỉ trường hợp đầu mới xóa người đã chỉ định trước đó.
        settle(selectList.map(x => ({ stepOrder: +x.dataset.stepOrder, userId: +x.value || 0 })));
    });
    document.getElementById('approver-cancel').addEventListener('click', () => settle(null));
    document.getElementById('approver-close').addEventListener('click', () => settle(null));
    document.getElementById('approver-modal').addEventListener('mousedown', (e) => {
        approverMaskMouseDown = e.target.id === 'approver-modal';
    });
    document.getElementById('approver-modal').addEventListener('click', (e) => {
        if (approverMaskMouseDown && e.target.id === 'approver-modal') settle(null);
    });
}

/**
 * Hộp thoại chọn người duyệt cho các bước của luồng cấp duyệt.
 *
 * Chỉ Admin dùng tới, và luôn kèm stepOrder — tức là luôn hỏi đúng MỘT bước đang chờ. Vẫn giữ nhánh hiển thị
 * nhiều bước: hàm này vốn dựng cho cả việc chỉ định lúc gửi duyệt, và tách đôi ra chỉ để bỏ một nhánh thì
 * không đáng.
 *
 * @param {Array} optionList Kết quả GET /course/{id}/step-approver-option.
 * @param {{title?: string, message?: string, okText?: string, stepOrder?: number}} [options]
 *        stepOrder: chỉ hỏi đúng một bước (dùng khi đổi người duyệt của bước đang chờ).
 * @returns {Promise<Array<{stepOrder: number, userId: number}>|null>} Danh sách đã chọn, hoặc null nếu bấm Hủy.
 */
function pickApproverDialog(optionList, options = {}) {
    ensureApproverModal();

    const visibleList = options.stepOrder == null
        ? optionList
        : optionList.filter(x => x.stepOrder === options.stepOrder);

    document.getElementById('approver-title').textContent = options.title || t('course.chiDinhNguoiDuyet', 'Chỉ định người duyệt');
    document.getElementById('approver-message').textContent = options.message || '';
    document.getElementById('approver-error').style.display = 'none';
    document.getElementById('approver-ok').textContent = options.okText || t('shell.dongY', 'Đồng ý');
    // Đổi người duyệt là hành động "giao bước này cho ai", nên ô chọn bắt buộc phải có người.
    document.getElementById('approver-step-list').innerHTML =
        visibleList.map(step => approverStepHtml(step, options.stepOrder != null)).join('');

    // Ô chọn có tìm kiếm: phòng ban đông người thì danh sách thả xuống dài tới mức phải cuộn mỏi tay.
    document.querySelectorAll('#approver-step-list [data-step-order]').forEach(select =>
        enhanceSearchableSelect(select, { placeholder: t('course.goTenNguoiDuyet', 'Gõ tên người duyệt...') }));

    openModal('approver-modal');
    return new Promise((resolve) => { approverResolve = resolve; });
}

/**
 * Một bước trong hộp thoại. Bước nào cũng có ô chọn người duyệt, trừ bước Admin đã ghim sẵn người (chỉ hiện
 * tên) và bước không có ai đủ điều kiện (hiện lý do).
 *
 * @param {boolean} requireAll Bắt buộc chọn ở mọi bước, kể cả bước vốn gửi cho cả nhóm — dùng khi đổi người
 *                             duyệt, vì hành động đó chính là "giao bước này cho một người cụ thể".
 */
function approverStepHtml(step, requireAll = false) {
    const head = `<div style="font-weight:600">${esc(step.stepOrder)}. ${esc(step.stepName)}</div>`;
    const box = (inner) => `<div class="mb" style="padding:10px 0;border-bottom:1px dashed var(--border,#e2e8f0)">${head}${inner}</div>`;

    if (step.approverMode === 'FixedUser') {
        const name = step.candidateList[0]?.fullName;
        return box(`<div class="small muted mt">${esc(t('course.coDinhNguoiDuyet', 'Cố định'))}: ${esc(name || '—')}</div>`);
    }

    // Nói rõ lý do ngay tại chỗ, vì bấm gửi sẽ bị server chặn với đúng thông báo này mà người soạn không biết
    // phải sửa ở đâu.
    if (!step.candidateList.length)
        return box(`<div class="small mt" style="color:#dc2626">${esc(t('course.khongCoAiDuyetBuocNay',
            'Không có ai đủ điều kiện duyệt bước này (sai vai trò / phòng ban, hoặc tài khoản đã ngừng hoạt động). Hãy báo Admin.'))}</div>`);

    const required = requireAll || step.approverMode === 'PickOne';
    const blankLabel = required
        ? t('course.chonNguoiDuyet', '— Chọn người duyệt —')
        : t('course.guiChoTatCaNguoiGiuVaiTro', '— Tất cả người giữ vai trò —');

    const optionHtml = step.candidateList.map(x => {
        const detail = [x.positionName, x.departmentName].filter(Boolean).join(' · ');
        return `<option value="${x.id}"${x.id === step.selectedUserId ? ' selected' : ''}>${esc(x.fullName)}${detail ? ' — ' + esc(detail) : ''}</option>`;
    }).join('');

    return box(`<select class="mt" data-step-order="${step.stepOrder}"${required ? ' data-required="1"' : ''}
                        style="width:100%" aria-label="${esc(step.stepName)}">
                    <option value="">${esc(blankLabel)}</option>
                    ${optionHtml}
                </select>`);
}

// ==================== BỐ CỤC ====================
const MENU = [
    { key: 'dashboard',    href: '/pages/dashboard.html',   icon: '<i class="ph ph-gauge"></i>', label: t('menu.dashboard', 'Tổng quan'),        role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { group: t('group.learning', 'Học tập'),    groupKey: 'learning', role: ['Học viên', 'Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { key: 'my-course',    href: '/pages/my-course.html',   icon: '<i class="ph ph-graduation-cap"></i>', label: t('menu.my-course', 'Khóa học của tôi'), role: ['Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { key: 'ai-tutor',     href: '/pages/ai-tutor.html',    icon: '<i class="ph ph-chalkboard-teacher"></i>', label: t('menu.ai-tutor', 'HB Minerva'), role: ['Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { key: 'certificate',  href: '/pages/certificate.html', icon: '<i class="ph ph-seal-check"></i>', label: t('index.stat-cert', 'Chứng nhận'),       role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { key: 'ranking',      href: '/pages/ranking.html',     icon: '<i class="ph ph-trophy"></i>', label: t('menu.ranking', 'Xếp hạng'),         role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { key: 'shop',         href: '/pages/shop.html',        icon: '<i class="ph ph-storefront"></i>', label: t('menu.shop', 'Trung Tâm Điểm Thưởng'),          role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { key: 'online-class', href: '/pages/online-class.html',icon: '<i class="ph ph-video-camera"></i>', label: t('menu.online-class', 'Phòng học online'), role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { group: t('group.training', 'Đào tạo'),    groupKey: 'training', role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { key: 'course',       href: '/pages/course.html',      icon: '<i class="ph ph-books"></i>', label: t('menu.course', 'Quản lý khóa học'), role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Trưởng phòng', 'Giám Đốc'] },
    { key: 'approval-workflow', href: '/pages/approval-workflow.html', icon: '<i class="ph ph-git-branch"></i>', label: t('menu.approval-workflow', 'Luồng cấp duyệt'), role: ['Admin', 'Trưởng Nhóm', 'Trưởng phòng', 'Giám Đốc'] },
    { key: 'sim-library',  href: '/pages/sim-library.html', icon: '<i class="ph ph-cube"></i>', label: t('menu.sim-library', 'Thư viện 3D'), role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên'] },
    { key: 'report',       href: '/pages/report.html',      icon: '<i class="ph ph-chart-line-up"></i>', label: t('menu.report', 'Báo cáo'),          role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] },
    { group: t('group.system', 'Hệ thống'),   groupKey: 'system',   role: ['Admin'] },
    { key: 'user',         href: '/pages/user.html',        icon: '<i class="ph ph-users"></i>', label: t('menu.user', 'Người dùng'),       role: ['Admin'] },
    { key: 'organization', href: '/pages/organization.html',icon: '<i class="ph ph-buildings"></i>', label: t('menu.organization', 'Phòng ban & Vị trí'), role: ['Admin'] },
    { key: 'role',         href: '/pages/role.html',        icon: '<i class="ph ph-shield-check"></i>', label: t('menu.role', 'Vai trò & Phân quyền'), role: ['Admin'] },
    { key: 'translation',  href: '/pages/translation.html', icon: '<i class="ph ph-translate"></i>', label: t('menu.translation', 'Dịch ngôn ngữ'), role: ['Admin'] },
    { key: 'ai-document', href: '/pages/ai-document.html', icon: '<i class="ph ph-brain"></i>', label: t('menu.ai-document', 'Tài liệu AI'), role: ['Admin'] },
    { key: 'information',  href: '/pages/account.html', icon: '<i class="ph ph-gear"></i>', label: t('menu.information', 'Thông tin'),          role: ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'] }
];



/**
 * Dựng sidebar + topbar. Gọi ở đầu mỗi trang.
 * @param {string} activeKey khóa menu đang mở
 * @param {string} title tiêu đề trang
 * @param {string} subtitle mô tả ngắn
 */
/** Nạp thư viện Phosphor Icons (dùng cho icon chuyên nghiệp thay Emoji) — chỉ nạp 1 lần dù mountLayout chạy nhiều trang. */
/** Nạp icon Phosphor từ file cục bộ (wwwroot/css/phosphor) — không gọi CDN bên thứ ba nào. */
function ensurePhosphorIcons() {
    if (document.getElementById('phosphor-icons-regular')) return;
    for (const weight of ['regular', 'fill', 'bold', 'duotone']) {
        const link = document.createElement('link');
        link.id = `phosphor-icons-${weight}`;
        link.rel = 'stylesheet';
        link.href = `/css/phosphor/${weight}.css`;
        document.head.appendChild(link);
    }
}

/** Thao tác khả dụng của từng chức năng. Không khai báo ở đây = chỉ có 'view'. */
const FUNCTION_ACTION = {
    course:         ['view', 'create', 'update', 'delete'],
    'sim-library':  ['view', 'create', 'update', 'delete'],
    user:           ['view', 'create', 'update', 'delete'],
    organization:   ['view', 'create', 'update', 'delete'],
    role:           ['view', 'create', 'update', 'delete'],
    'approval-workflow': ['view', 'create', 'update', 'delete'],
    translation:    ['view', 'create', 'update', 'delete'],
    'online-class': ['view', 'create', 'update', 'delete'],
    'ai-document':  ['view', 'create', 'delete'],
    report:         ['view', 'export'],
    certificate:    ['view', 'export'],
    ranking:        ['view', 'update'],
    shop:           ['view', 'create', 'update', 'delete'],
    information:    ['view', 'update']
};

/** Nhãn thao tác CRUD. Cũng phải là hàm vì lý do như approvalStepList(). */
function actionLabel(action) {
    return {
        view:   t('shell.xem', 'Xem'),
        create: t('organization.them', 'Thêm'),
        update: t('common.edit', 'Sửa'),
        delete: t('common.delete', 'Xóa'),
        export: t('shell.xuatFile', 'Xuất file')
    }[action] || action;
}

/** Toàn bộ chức năng + thao tác có thể cấp — suy ra từ MENU, BỎ các dòng chỉ có group (tiêu đề nhóm). */
function getFunctionList() {
    return MENU.filter(item => !item.group).map(item => ({
        key: item.key,
        label: item.label,
        icon: item.icon,
        actionList: FUNCTION_ACTION[item.key] || ['view']
    }));
}

// ==================== BẢNG TICK QUYỀN (dùng chung cho trang Vai trò và trang Người dùng) ====================

/** Vẽ danh sách chức năng vào phần tử id đã cho: ô tick cha + khối thao tác CRUD xổ xuống. */
function renderPermissionMatrix(containerId) {
    document.getElementById(containerId).innerHTML = getFunctionList().map(fn => `
        <div class="perm-item" data-key="${fn.key}">
            <div class="perm-head">
                <label class="check">
                    <input type="checkbox" class="perm-module">
                    <span>${fn.icon} ${esc(fn.label)}</span>
                </label>
                <button type="button" class="perm-toggle" data-i18n-title="user.xemChiTietQuyen" title="Xem chi tiết quyền"><i class="ph ph-caret-down"></i></button>
            </div>
            <div class="perm-body hidden">
                ${fn.actionList.map(action => `
                    <label class="check">
                        <input type="checkbox" class="perm-action" data-key="${fn.key}.${action}" data-action="${action}">
                        <span>${actionLabel(action)}</span>
                    </label>`).join('')}
            </div>
        </div>`).join('');
}

/** Ô cha: tick đủ con = tick, tick một phần = gạch ngang, không con nào = trống.
 *
 *  Thao tác bị KHÓA (vai trò không có quyền đó — xem setUserPermissionMatrix) không được tính vào mẫu số:
 *  nếu tính, một chức năng đã cấp trọn phần mà vai trò cho phép vẫn hiện gạch ngang như đang cấp dở. */
function syncPermissionModule(item) {
    const actionList = [...item.querySelectorAll('.perm-action')];
    const openList = actionList.filter(x => !x.disabled);
    const countList = openList.length ? openList : actionList;

    const checkedCount = countList.filter(x => x.checked).length;
    const module = item.querySelector('.perm-module');
    module.checked = checkedCount === countList.length;
    module.indeterminate = checkedCount > 0 && checkedCount < countList.length;
}

/** Gắn sự kiện 1 lần cho cả khối (ủy quyền sự kiện — vẽ lại bao nhiêu lần cũng không phải gắn lại). */
function bindPermissionMatrix(containerId) {
    const box = document.getElementById(containerId);

    box.addEventListener('click', e => {
        const toggle = e.target.closest('.perm-toggle');
        if (!toggle) return;
        const item = toggle.closest('.perm-item');
        item.classList.toggle('open');
        item.querySelector('.perm-body').classList.toggle('hidden');
    });

    box.addEventListener('change', e => {
        const item = e.target.closest('.perm-item');
        if (!item) return;

        // Tick ô cha = tick/bỏ tick toàn bộ thao tác con, đồng thời mở khối ra cho thấy đã cấp gì.
        if (e.target.classList.contains('perm-module')) {
            item.querySelectorAll('.perm-action:not(:disabled)').forEach(x => x.checked = e.target.checked);
            if (e.target.checked) {
                item.classList.add('open');
                item.querySelector('.perm-body').classList.remove('hidden');
            }
        }

        // Cấp Thêm/Sửa/Xóa mà không có Xem là vô nghĩa (không vào được trang) — tự tick Xem kèm theo.
        if (e.target.classList.contains('perm-action') && e.target.checked && e.target.dataset.action !== 'view') {
            const view = item.querySelector('.perm-action[data-action="view"]');
            if (view && !view.disabled) view.checked = true;
        }

        syncPermissionModule(item);
    });
}

/** Đổ quyền đã lưu lên giao diện + thu gọn lại toàn bộ. readOnly = khóa không cho sửa. */
function setPermissionMatrix(containerId, raw, readOnly = false) {
    const box = document.getElementById(containerId);
    const keyList = parseFunctionKeys(raw) || [];

    box.querySelectorAll('.perm-action').forEach(input => {
        // Dữ liệu cũ lưu key trần ("course") được hiểu là có toàn quyền chức năng đó.
        input.checked = keyList.includes(input.dataset.key) || keyList.includes(input.dataset.key.split('.')[0]);
        input.disabled = readOnly;
    });

    box.querySelectorAll('.perm-item').forEach(item => {
        syncPermissionModule(item);
        item.querySelector('.perm-module').disabled = readOnly;
        item.classList.remove('open');
        item.querySelector('.perm-body').classList.add('hidden');
    });
}

/** Mở rộng khóa trần ("course") thành "course.view" — đúng như Helpers/FunctionKey.Expand() ở server.
 *  Chỉ còn gặp ở dữ liệu cũ: từ khi có FunctionKey.Normalize(), mọi bản ghi lưu xuống đều đủ dạng
 *  "chức năng.thao tác". */
function expandFunctionKey(key) { return key.includes('.') ? key : `${key}.view`; }

/** Danh sách quyền đã chuẩn hóa từ chuỗi CSV. Rỗng = chưa cấu hình gì. */
function functionKeyList(raw) { return (parseFunctionKeys(raw) || []).map(expandFunctionKey); }

/**
 * Đổ quyền lên bảng tick của biểu mẫu NGƯỜI DÙNG — khác setPermissionMatrix() ở chỗ có tính tới vai trò.
 *
 * Quyền thực tế của một người là GIAO của quyền vai trò và quyền cấp riêng (Helpers/FunctionKey.Effective),
 * nghĩa là phần cấp riêng chỉ thu hẹp được, không mở rộng. Bảng tick trước đây không thể hiện điều đó:
 * Admin tick một chức năng mà vai trò không có, bấm Lưu, hệ thống nhận nhưng người dùng vẫn không vào được —
 * không có gì trên màn hình nói tại sao.
 *
 * Nên ở đây:
 *   • Quyền vai trò KHÔNG có  -> khóa ô lại, tô mờ. Tick vào cũng vô nghĩa thì đừng cho tick.
 *   • Chưa cấp riêng          -> tick sẵn đúng bộ quyền của vai trò, tức là quyền người này đang thực sự có.
 *   • Đã cấp riêng            -> tick phần giao, phần bị thu hẹp hiện ra dưới dạng ô trống.
 *
 * Vai trò chưa cấu hình quyền nào thì hệ thống chặn theo TÊN vai trò như cũ, không có gì để giới hạn —
 * lúc đó mở khóa toàn bộ và để trang tự hiện lời nhắc.
 *
 * @param {string} containerId id khối .perm-list
 * @param {string|null} roleRaw chuỗi CSV quyền của vai trò đang chọn
 * @param {string|null} userRaw chuỗi CSV quyền cấp riêng đã lưu của người dùng (null = kế thừa vai trò)
 * @returns {boolean} vai trò đã được cấu hình quyền hay chưa — để trang quyết định hiện lời nhắc nào
 */
function setUserPermissionMatrix(containerId, roleRaw, userRaw) {
    const box = document.getElementById(containerId);
    const roleKeyList = functionKeyList(roleRaw);
    const userKeyList = functionKeyList(userRaw);
    const roleConfigured = roleKeyList.length > 0;

    const effectiveList = !roleConfigured
        ? userKeyList
        : (userKeyList.length ? roleKeyList.filter(x => userKeyList.includes(x)) : roleKeyList);

    box.querySelectorAll('.perm-action').forEach(input => {
        const allowedByRole = !roleConfigured || roleKeyList.includes(input.dataset.key);
        input.disabled = !allowedByRole;
        input.checked = allowedByRole && effectiveList.includes(input.dataset.key);
    });

    box.querySelectorAll('.perm-item').forEach(item => {
        // Cả chức năng nằm ngoài vai trò thì gập lại và tô mờ nguyên khối, khỏi phải mở ra mới biết.
        const lockedAll = [...item.querySelectorAll('.perm-action')].every(x => x.disabled);
        item.classList.toggle('locked', lockedAll);
        item.querySelector('.perm-module').disabled = lockedAll;
        // Nhãn nhỏ bên phải, trả lời luôn câu "sao chỗ này tick không được" — style ở .perm-item.locked.
        item.querySelector('.perm-head').dataset.lockedLabel = t('user.vaiTroKhongCoQuyenNay', 'Vai trò không có');

        syncPermissionModule(item);
        item.classList.remove('open');
        item.querySelector('.perm-body').classList.add('hidden');
    });

    return roleConfigured;
}

/** Gom quyền đang tick thành chuỗi CSV gửi API. */
function getPermissionMatrix(containerId) {
    return [...document.querySelectorAll(`#${containerId} .perm-action:checked`)].map(x => x.dataset.key).join(',') || null;
}

/** Có được vào chức năng này không. Chấp nhận cả dữ liệu cũ lưu key trần ("course") lẫn key mới ("course.view"). */
function hasFunctionView(allowedKeyList, key) {
    return allowedKeyList.includes(key) || allowedKeyList.includes(`${key}.view`);
}

/** Tách chuỗi CSV "dashboard,report" thành mảng. Trả null nếu rỗng — null = không giới hạn riêng. */
function parseFunctionKeys(raw) {
    if (!raw) return null;
    const keyList = String(raw).split(',').map(x => x.trim()).filter(Boolean);
    return keyList.length ? keyList : null;
}

/** Bộ icon Flaticon Uicons (kiểu "regular rounded"), dùng song song Phosphor — gõ <i class="fi-rr-ten-icon"></i>.
 * Tra tên icon tại flaticon.com/uicons (bản "Regular Rounded"). File font tải sẵn về máy chủ, không gọi CDN ngoài lúc chạy. */
function ensureUicons() {
    if (document.getElementById('uicons-regular-rounded')) return;
    const link = document.createElement('link');
    link.id = 'uicons-regular-rounded';
    link.rel = 'stylesheet';
    link.href = '/css/uicons/uicons-regular-rounded.css';
    document.head.appendChild(link);
}

/**
 * Lọc MENU thành danh sách thực sự hiển thị: giữ mục con mà người dùng được vào, và chỉ giữ tiêu đề
 * nhóm khi trong nhóm đó còn ít nhất một mục con — tránh tiêu đề nhóm đứng trơ không có gì bên dưới.
 */
function visibleMenuList(canOpen, groupAllowed) {
    const visibleList = MENU.map(item => item.group ? item : (canOpen(item) ? item : null));

    return visibleList.filter((item, index) => {
        if (!item) return false;
        if (!item.group) return true;
        if (!groupAllowed(item)) return false;

        // Tiêu đề nhóm: nhìn xuống tới tiêu đề nhóm kế tiếp, còn mục con nào hiện được thì mới giữ.
        for (let next = index + 1; next < visibleList.length; next++) {
            if (MENU[next].group) break;
            if (visibleList[next]) return true;
        }
        return false;
    });
}

/**
 * Làm mới ngầm hồ sơ đang lưu trong trình duyệt.
 *
 * Vì sao cần: hbe_profile chỉ được ghi ĐÚNG MỘT LẦN lúc đăng nhập. Thêm một màn hình mới vào hệ thống thì quyền
 * của vai trò có thêm khóa mới (xem FunctionKey.AddNewModuleKey), nhưng người đang đăng nhập vẫn giữ bản hồ sơ cũ —
 * menu mới không bao giờ hiện ra cho tới khi họ tự đăng xuất rồi đăng nhập lại, mà chẳng có gì gợi ý là phải làm vậy.
 * Cả cờ được ủy quyền duyệt / được giao bước trong luồng cấp duyệt cũng vậy.
 *
 * Ghi hồ sơ mới TRƯỚC rồi mới so sánh, nên lần tải sau chắc chắn khớp — không có vòng lặp tải lại.
 */
async function refreshStoredProfile() {
    if (!Auth.isLoggedIn) return;

    // Phần hồ sơ quyết định menu và quyền — chỉ những thứ này đổi mới đáng tải lại trang.
    const signature = (p) => JSON.stringify([
        p?.roleName, p?.parentRoleName, p?.roleFunctionKeys, p?.functionKeys,
        !!p?.isApprovalDelegate, !!p?.isWorkflowApprover
    ]);
    const before = signature(Auth.profile);

    let fresh;
    try { fresh = await API.getSilent('/auth/me'); } catch { return; }

    Auth.saveProfile(fresh);
    if (signature(fresh) !== before) location.reload();
}

function mountLayout(activeKey, title, subtitle = '') {
    ensurePhosphorIcons();
    ensureUicons();

    const profile = Auth.profile;
    if (!profile) { location.href = '/login.html'; return; }

    // Mọi tên vai trò người này được coi là đang giữ — gồm cả vai trò CHA nếu vai trò của họ kế thừa
    // ("Trưởng phòng IT" cũng là "Trưởng phòng"). Không xét vai trò cha thì menu của cha bị giấu sạch,
    // trong khi API vẫn cho vào — người dùng thấy một hệ thống trống rỗng mà không hiểu vì sao.
    const roleMatches = (allowedRoleList) => allowedRoleList.some(x => Auth.hasRole(x));
    // Người được ủy quyền duyệt bước cuối (nhân viên cùng phòng ban với Giám Đốc Nhân sự, có thể không giữ vai trò nào trong
    // danh sách role bên dưới) vẫn cần thấy mục "Quản lý khóa học" để tìm và duyệt khóa học được giao.
    // Người được giao một bước trong luồng cấp duyệt do Admin định nghĩa cũng vậy: vai trò của họ có thể là bất
    // cứ vai trò nào Admin chọn, kể cả vai trò tự tạo không nằm trong danh sách role của MENU.
    const isApprovalDelegate = !!profile.isApprovalDelegate || !!profile.isWorkflowApprover;
    
    // Quyền thực tế: quyền của VAI TRÒ (Admin cấu hình ở trang "Vai trò & Phân quyền") GIAO với quyền
    // cấp riêng cho từng người — quyền cá nhân chỉ thu hẹp, không bao giờ mở rộng thêm.
    // Vai trò chưa cấu hình quyền (null) thì giữ nguyên hành vi cũ: lọc menu theo tên vai trò.
    const allowedFunctionKeyList = Auth.functionKeyList;

    const canOpen = (item) => (allowedFunctionKeyList
            ? hasFunctionView(allowedFunctionKeyList, item.key)
            : roleMatches(item.role))
        // Người được ủy quyền duyệt bước cuối luôn cần vào được "Quản lý khóa học" để duyệt.
        || (isApprovalDelegate && item.key === 'course');

    // Tiêu đề nhóm: vai trò cài sẵn giữ nguyên cách lọc theo vai trò như trước (VD Học viên không thấy
    // tiêu đề "Hệ thống" dù vẫn có mục "Thông tin" nằm dưới nó). Vai trò do Admin tự tạo không nằm trong
    // danh sách vai trò của MENU nên hiện mọi tiêu đề còn mục con — nếu không sẽ mất hết tiêu đề nhóm.
    const isBuiltInRole = MENU.some(item => roleMatches(item.role));
    const groupAllowed = (item) => !isBuiltInRole || roleMatches(item.role);

    // Dùng cho viên điểm trên thanh trên (xem bên dưới) — cùng một phép kiểm với mục menu "Cửa hàng",
    // để không bao giờ có chuyện bấm vào viên điểm rồi bị màn hình cửa hàng đuổi ra.
    const canOpenShop = canOpen(MENU.find(x => x.key === 'shop'));

    const navHtml = visibleMenuList(canOpen, groupAllowed)
        .map(item => item.group
            ? `<div class="nav-group">${esc(t(`group.${item.groupKey}`, item.group))}</div>`
            : `<a href="${item.href}" class="${item.key === activeKey ? 'active' : ''}" title="${esc(t(`menu.${item.key}`, item.label))}">
                   <span class="ico">${item.icon}</span><span class="label">${esc(t(`menu.${item.key}`, item.label))}</span>
               </a>`)
        .join('');

    // Đặt lớp thu gọn trước khi chèn sidebar vào DOM — làm sau thì thanh bên hiện ra rộng rồi mới co lại.
    applySidebarCollapsed(isSidebarCollapsed());

    document.body.insertAdjacentHTML('afterbegin', `
        <div class="sidebar-backdrop" id="sidebarBackdrop" onclick="toggleMobileSidebar(false)"></div>
        <aside class="sidebar" id="sidebar">
            <div class="sidebar-brand" >
                <img src="/img/HoaBinh.png" alt="">
                <div class="txt">
                    <h2>HB E-Learning</h2>
                    <span>${esc(t('shell.subtitle', 'Đào tạo nội bộ'))}</span>
                </div>
                <button class="sidebar-toggle" id="sidebar-toggle-btn" type="button"
                        onclick="toggleSidebarCollapsed()"><i class="ph-bold ph-caret-double-left"></i></button>
            </div>
            <nav class="sidebar-nav">${navHtml}</nav>
            <div class="sidebar-user">
                <div class="sidebar-user-top">
                    <a class="sidebar-user-link" href="/pages/account.html">
                        ${profile.avatarUrl ? `<img class="avatar" src="${esc(profile.avatarUrl)}" alt="">` : `<div class="avatar">${esc(initial(profile.fullName))}</div>`}
                        <div class="info">
                            <strong>${esc(profile.fullName)}</strong>
                            <small id="sidebar-user-sub">${esc(roleLabel(profile.roleName))}${profile.departmentName ? ' · ' + esc(profile.departmentName) : ''}</small>
                        </div>
                    </a>
                    <button class="close-x logout-btn" title="${esc(t('shell.logout', 'Đăng xuất'))}" onclick="Auth.logout()">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>
                    </button>
                </div>
                <!-- Thanh kinh nghiệm đổ vào sau bằng mountRankBadge(): số liệu nằm ở API xếp hạng, mà
                     mountLayout thì chạy đồng bộ. Ẩn sẵn để không chớp một thanh rỗng rồi mới có số. -->
                <div class="sidebar-xp hidden" id="sidebar-xp">
                    <div class="sidebar-xp-head">
                        <span>${esc(t('shell.experience', 'Kinh nghiệm'))}</span>
                        <b id="sidebar-xp-text"></b>
                    </div>
                    <div class="sidebar-xp-bar"><span id="sidebar-xp-fill" style="width:0%"></span></div>
                </div>
            </div>
        </aside>`);

    // Người dùng bàn phím: phím Tab đầu tiên trên mọi trang dừng ở liên kết này, khỏi phải đi qua cả menu bên
    // trái lẫn hàng nút trên thanh trên mới tới được nội dung. Phải đứng TRƯỚC thanh bên trong DOM (chèn sau
    // thanh bên, cũng bằng 'afterbegin') — để trong thanh trên thì nó xếp sau hai chục mục menu. Ẩn cho tới khi
    // được focus, xem .skip-link.
    document.body.insertAdjacentHTML('afterbegin',
        `<a class="skip-link" href="#" id="skip-to-content">${esc(t('shell.skipToContent', 'Bỏ qua tới nội dung chính'))}</a>`);

    // Ô biểu tượng cạnh tiêu đề trang: đúng icon của mục menu đang sáng bên trái, nét hai tông (duotone) cho
    // đậm hơn bản nét mảnh trong menu. Vừa cho khối tiêu đề một điểm tựa thị giác (trước đây chỉ là hai dòng chữ
    // trơn), vừa nối tiêu đề với mục menu — liếc qua là biết mình đang ở đâu. Trang không có mục menu riêng
    // (VD trang học bài) thì không có ô, tiêu đề đứng một mình như cũ.
    const activeItem = MENU.find(x => x.key === activeKey && x.icon);
    const pageIconHtml = activeItem
        ? `<span class="topbar-icon" aria-hidden="true">${activeItem.icon.replace('class="ph ', 'class="ph-duotone ')}</span>`
        : '';

    const main = document.querySelector('.main');
    if (main) {
        main.insertAdjacentHTML('afterbegin', `
            <header class="topbar" id="topbar">
                <!-- Giữ lớp .flex và cặp h1 / .sub: nhiều trang tự thay tiêu đề bằng querySelector('.topbar h1')
                     và chèn lại '.topbar .sub' ngay sau nó (course-detail, learn, quiz, classroom-recap). -->
                <div class="flex topbar-title">
                    <button class="mobile-menu-btn" type="button" title="${esc(t('shell.menu', 'Menu'))}"
                            aria-label="${esc(t('shell.menu', 'Menu'))}" onclick="toggleMobileSidebar()"><i class="ph ph-list"></i></button>
                    ${pageIconHtml}
                    <div class="topbar-heading">
                        <h1>${esc(title)}</h1>
                        ${subtitle ? `<div class="sub" title="${esc(subtitle)}">${esc(subtitle)}</div>` : ''}
                    </div>
                </div>
                <div class="topbar-search" role="search">
                    <i class="ph ph-magnifying-glass"></i>
                    <input type="search" id="topbar-search-input" autocomplete="off"
                           value="${esc(qs('keyword') || '')}"
                           aria-label="${esc(t('shell.search', 'Tìm kiếm'))}"
                           placeholder="${esc(t('shell.searchPlaceholder', 'Tìm khóa học,...'))}">
                    <kbd class="topbar-kbd" aria-hidden="true">${/Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent) ? '⌘ K' : 'Ctrl K'}</kbd>
                </div>
                <!-- Chỉ hiện trên màn hẹp, nơi ô tìm kiếm bị cất đi: bấm là ô trải ra thành một dòng riêng (xem
                     mountTopbarSearch). Cố ý đứng NGOÀI khối tiện ích: trên điện thoại nó nằm cuối dòng tiêu đề,
                     để dòng nút thứ hai đủ chỗ cho cả khối tiện ích lẫn nút của trang. -->
                <button class="topbar-search-toggle" id="topbar-search-toggle" type="button"
                        title="${esc(t('shell.search', 'Tìm kiếm'))}" aria-label="${esc(t('shell.search', 'Tìm kiếm'))}"
                        aria-controls="topbar-search-input" aria-expanded="false">
                    <i class="ph ph-magnifying-glass"></i>
                </button>
                <!-- Bên phải: hai KHỐI LIỀN rồi tới hành động riêng của trang. Khối số liệu (chuỗi ngày, điểm)
                     và khối tiện ích (tìm kiếm trên điện thoại, chat, chuông, sáng/tối, ngôn ngữ) mỗi khối chung
                     một nền một viền, các nút bên trong không viền riêng. Trước đây là sáu ô có viền đứng cách đều
                     nhau thành một hàng rào; gom lại thì mắt đọc ra hai nhóm, và nút chính của trang (sau vạch
                     ngăn) là thứ duy nhất còn nổi khối. -->
                <div class="topbar-right">
                  <div class="topbar-tools">
                    <!-- Hai viên chỉ số đổ vào sau, cùng lượt gọi API với thanh kinh nghiệm. Chưa viên nào hiện
                         thì CSS giấu luôn cái khối rỗng (xem .topbar-stats). -->
                    <div class="topbar-stats">
                        <span class="topbar-pill streak hidden" id="pill-streak" title="${esc(t('shell.streakTitle', 'Số ngày học liên tiếp'))}">
                            <i class="ph-fill ph-fire" data-force-emoji></i><b id="pill-streak-value">0</b>
                        </span>
                        <!-- Viên điểm dẫn thẳng vào Cửa hàng khi người này vào được đó: điểm chỉ có ý nghĩa khi
                             tiêu được, mà chỗ tiêu lại nằm sâu trong menu bên trái. Vai trò không được vào cửa
                             hàng thì nó vẫn là một viên chỉ số tĩnh như trước. -->
                        <span class="topbar-pill point hidden${canOpenShop ? ' clickable' : ''}" id="pill-point"
                              ${canOpenShop ? `onclick="location.href='/pages/shop.html'"` : ''}
                              title="${esc(canOpenShop
                                  ? t('shell.pointTitleShop', 'Điểm tích lũy — bấm để vào Trung Tâm đổi quà')
                                  : t('shell.pointTitle', 'Điểm tích lũy'))}">
                            <i class="ph-fill ph-diamond" data-force-emoji></i><b id="pill-point-value">0</b>
                        </span>
                    </div>
                  <div class="topbar-dock">
                    <!-- Hộp chat nội bộ. Dùng lại class .notif-btn/.notif-badge của chuông chứ không tự
                         đặt kiểu riêng: hai nút đứng cạnh nhau mà lệch vài pixel là thấy ngay, và mỗi lần
                         chỉnh khuôn nút tiện ích lại phải nhớ sửa hai chỗ. Bảng chat do js/chat.js đổ vào
                         trong thẻ bọc này (nó cần .notif-wrapper làm mốc position:relative để xổ xuống). -->
                    <div class="notif-wrapper" id="chat-bell">
                        <button class="notif-btn" id="chat-topbar-btn" type="button" title="${esc(t('shell.chat', 'Tin nhắn'))}"
                                aria-label="${esc(t('shell.chat', 'Tin nhắn'))}">
                            <i class="ph ph-chat-circle-dots"></i>
                            <span class="notif-badge hidden" id="chat-badge">0</span>
                        </button>
                    </div>

                    <div class="notif-wrapper" id="notif-bell">
                        <button class="notif-btn" id="notif-bell-btn" type="button" title="${esc(t('shell.notification', 'Thông báo'))}"
                                aria-label="${esc(t('shell.notification', 'Thông báo'))}">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path>
                                <path d="M13.73 21a2 2 0 0 1-3.46 0"></path>
                            </svg>
                            <span class="notif-badge hidden" id="notif-badge">0</span>
                        </button>
                        <div class="notif-dropdown hidden" id="notif-dropdown">
                            <div class="notif-header">
                                <h3>${esc(t('shell.notifTitleShort', 'Thông báo'))}</h3>
                                <span class="notif-new-badge hidden" id="notif-new-badge"></span>
                            </div>
                            <div class="notif-tabs">
                                <button type="button" class="notif-tab active" id="notif-tab-all">${esc(t('dashboard.tatCa', 'Tất cả'))}</button>
                                <button type="button" class="notif-tab" id="notif-tab-unread">
                                    ${esc(t('shell.notifUnread', 'Chưa đọc'))} <span class="n" id="notif-tab-unread-count">0</span>
                                </button>
                                <button class="notif-mark-all-btn" id="notif-mark-all" type="button">
                                    <i class="ph-bold ph-checks"></i> ${esc(t('shell.markAllReadShort', 'Đọc hết'))}
                                </button>
                            </div>
                            <div class="notif-list" id="notif-list"></div>
                            <div class="notif-footer hidden" id="notif-footer"><button type="button"></button></div>
                        </div>
                    </div>

                    <div class="text-size-switch" id="text-size-switch"></div>
                    <button class="theme-switch" type="button" id="theme-switch-btn"></button>
                    <button class="lang-switch" type="button" id="lang-switch-btn"></button>
                  </div>
                  </div>
                  <div class="topbar-action" id="topbar-action"></div>
                </div>
            </header>`);

        mountNotificationBell();
        mountTopbarSearch();
        mountTopbarChrome();
        mountRankBadge();
        renderThemeSwitch(document.getElementById('theme-switch-btn'));
        renderTextSizeSwitch(document.getElementById('text-size-switch'));
        // Phần bên trong nút và hành vi xổ xuống do language.js lo — xem renderLanguageSwitch().
        renderLanguageSwitch(document.getElementById('lang-switch-btn'));
    }

    syncSidebarToggleLabel();

    // Dịch các phần tử tĩnh của trang (thẻ có data-i18n) sau khi vỏ đã dựng xong.
    applyI18n();

    // Không await: vỏ trang đã dựng xong rồi, việc làm mới hồ sơ chạy nền — xem refreshStoredProfile().
    refreshStoredProfile();
    mountChatWidget();
}

// ==================== THU GỌN / MỞ RỘNG THANH BÊN ====================
// Trạng thái nằm ở localStorage chứ không phải biến trong bộ nhớ: mỗi lần chuyển trang là một lần tải lại
// trang, giữ trong bộ nhớ thì thanh bên bung ra lại ngay ở màn hình kế tiếp.
const SIDEBAR_COLLAPSED_KEY = 'hbe_sidebar_collapsed';

function isSidebarCollapsed() {
    try { return localStorage.getItem(SIDEBAR_COLLAPSED_KEY) === '1'; }
    catch { return false; }
}

/** Bật/tắt lớp thu gọn trên <body> — CSS lo phần còn lại qua biến --sidebar-width. */
function applySidebarCollapsed(collapsed) {
    document.body.classList.toggle('sidebar-collapsed', collapsed);
}

/** Đổi nhãn gợi ý của nút theo trạng thái hiện tại, để rê chuột vào biết bấm sẽ ra gì. */
function syncSidebarToggleLabel() {
    const btn = document.getElementById('sidebar-toggle-btn');
    if (!btn) return;
    btn.title = isSidebarCollapsed()
        ? t('shell.expandSidebar', 'Mở rộng menu')
        : t('shell.collapseSidebar', 'Thu gọn menu');
}

/** Thu gọn thanh bên còn dải icon, hoặc mở lại. Gọi từ nút trong khối thương hiệu. */
function toggleSidebarCollapsed(force) {
    const collapsed = typeof force === 'boolean' ? force : !isSidebarCollapsed();
    try { localStorage.setItem(SIDEBAR_COLLAPSED_KEY, collapsed ? '1' : '0'); } catch { /* chế độ riêng tư */ }
    applySidebarCollapsed(collapsed);
    syncSidebarToggleLabel();
}

// ==================== Ô TÌM KIẾM TRÊN THANH TRÊN ====================
/**
 * Gõ xong bấm Enter thì sang màn hình danh sách khóa học kèm từ khóa.
 *
 * Đưa về đúng màn hình mà người đó VÀO ĐƯỢC: quản trị/giảng viên có màn "Quản lý khóa học" xem được mọi
 * khóa, còn học viên chỉ có "Khóa học của tôi". Đẩy học viên sang trang họ không có quyền thì cú tìm kiếm
 * kết thúc bằng một thông báo từ chối truy cập.
 */
function mountTopbarSearch() {
    const input = document.getElementById('topbar-search-input');
    if (!input) return;

    const allowedKeyList = Auth.functionKeyList;
    const canOpenCourse = allowedKeyList
        ? hasFunctionView(allowedKeyList, 'course')
        : ['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Trưởng phòng', 'Giám Đốc'].some(x => Auth.hasRole(x));
    const targetPage = canOpenCourse ? '/pages/course.html' : '/pages/my-course.html';

    const topbar = document.getElementById('topbar');
    const toggle = document.getElementById('topbar-search-toggle');

    // Trên màn hẹp ô tìm kiếm bị cất đi, nút kính lúp trong khối tiện ích trải nó ra thành một dòng riêng.
    // Trên màn rộng lớp search-open không có tác dụng gì nên mở/đóng dùng chung một đường.
    const openSearch = () => {
        topbar?.classList.add('search-open');
        toggle?.setAttribute('aria-expanded', 'true');
        input.focus();
        input.select();
    };
    const closeSearch = () => {
        topbar?.classList.remove('search-open');
        toggle?.setAttribute('aria-expanded', 'false');
    };
    toggle?.addEventListener('click', () =>
        topbar?.classList.contains('search-open') ? closeSearch() : openSearch());

    const goCourseList = () => {
        const keyword = input.value.trim();
        location.href = keyword ? `${targetPage}?keyword=${encodeURIComponent(keyword)}` : targetPage;
    };
    const suggest = mountSearchSuggest(input, goCourseList);

    input.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            // Lần Esc đầu chỉ đóng ô gợi ý, lần sau mới rời ô tìm kiếm.
            if (suggest.isOpen()) { suggest.close(); return; }
            input.blur(); closeSearch(); return;
        }
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            if (!suggest.isOpen()) return;
            e.preventDefault();
            suggest.move(e.key === 'ArrowDown' ? 1 : -1);
            return;
        }
        if (e.key !== 'Enter') return;
        e.preventDefault();
        // Đang chọn một dòng gợi ý bằng phím mũi tên thì mở dòng đó; không thì giữ hành vi cũ: sang danh sách khóa học.
        if (suggest.pickActive()) return;
        goCourseList();
    });

    // Phím tắt Ctrl/⌘ + K và "/" — chỉ khi KHÔNG đang gõ ở đâu cả. Ctrl + K trong trình soạn thảo bài học
    // (Quill) là "chèn liên kết", còn "/" là một ký tự người ta gõ thật; cướp hai phím đó giữa chừng là phá
    // việc đang làm của người dùng.
    document.addEventListener('keydown', (e) => {
        const target = e.target;
        const isTyping = target instanceof Element
            && !!target.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"]');
        if (isTyping) return;
        const isShortcut = ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k')
            || (e.key === '/' && !e.ctrlKey && !e.metaKey && !e.altKey);
        if (!isShortcut) return;
        e.preventDefault();
        openSearch();
    });
}

// ==================== GỢI Ý KHI GÕ Ở Ô TÌM KIẾM ====================
/**
 * Ô xổ xuống dưới ô tìm kiếm: vài NGƯỜI và vài KHÓA HỌC khớp từ khóa (GET /search). Bấm một người là mở thẻ hồ sơ
 * của họ ngay trên trang đang đứng (openPersonCard), bấm một khóa là vào học / đăng ký.
 *
 * Khóa học ở đây đã được máy chủ lọc theo đối tượng (xem SearchService): khóa dành riêng cho một phòng ban chỉ
 * hiện với người của phòng ban đó, giao diện không lọc thêm gì.
 *
 * @param {HTMLInputElement} input ô tìm kiếm trên thanh trên.
 * @param {() => void} goCourseList chuyển sang trang danh sách khóa học kèm từ khóa (hành vi Enter trước đây).
 */
function mountSearchSuggest(input, goCourseList) {
    const box = document.createElement('div');
    box.className = 'ts-pop';
    box.id = 'ts-pop';
    box.setAttribute('role', 'listbox');
    box.hidden = true;
    input.parentElement.appendChild(box);
    input.setAttribute('aria-controls', 'ts-pop');
    input.setAttribute('aria-autocomplete', 'list');

    let itemList = [];      // [{ el, run }] theo thứ tự hiển thị — phím ↑↓ đi trên danh sách này
    let activeIndex = -1;
    let seq = 0;
    let timer = null;

    const close = () => { box.hidden = true; activeIndex = -1; input.setAttribute('aria-expanded', 'false'); };
    const open = () => { box.hidden = false; input.setAttribute('aria-expanded', 'true'); };

    const setActive = (index) => {
        activeIndex = index;
        itemList.forEach((x, i) => x.el.classList.toggle('active', i === index));
        itemList[index]?.el.scrollIntoView({ block: 'nearest' });
    };

    function render(keyword, result) {
        const personList = result?.personList ?? [];
        const courseList = result?.courseList ?? [];
        const avatar = (p) => p.avatarUrl
            ? `<img class="avatar" src="${esc(p.avatarUrl)}" alt="">`
            : `<span class="avatar">${esc(initial(p.fullName))}</span>`;

        box.innerHTML = `
            ${personList.length ? `<div class="ts-group">${esc(t('search.nguoi', 'Mọi người'))}</div>
                ${personList.map(p => `
                    <button type="button" class="ts-item" role="option" data-person="${p.id}">
                        ${avatar(p)}
                        <span class="ts-text"><b>${esc(p.fullName)}</b>
                            <small>${esc([p.employeeCode, p.positionName, p.departmentName].filter(Boolean).join(' · '))}</small></span>
                        <i class="ph ph-caret-right ts-go"></i>
                    </button>`).join('')}` : ''}
            ${courseList.length ? `<div class="ts-group">${esc(t('search.khoaHoc', 'Khóa học'))}</div>
                ${courseList.map(c => `
                    <button type="button" class="ts-item" role="option" data-course="${c.id}">
                        ${courseThumbHtml(c.title, c.thumbnailUrl, 'ts-thumb')}
                        <span class="ts-text"><b>${esc(c.title)}</b>
                            <small>${esc(c.code)} · ${esc(c.audience === 'Company'
                                ? t('search.toanCongTy', 'Toàn công ty') : t('search.theoPhongBan', 'Theo phòng ban'))}${c.authorName ? ' · ' + esc(c.authorName) : ''}</small></span>
                        <span class="ts-action">${esc(courseActionOf(c).label)}</span>
                    </button>`).join('')}` : ''}
            ${!personList.length && !courseList.length
                ? `<div class="ts-empty"><i class="ph ph-magnifying-glass"></i>${esc(t('search.khongThay', 'Không tìm thấy người hay khóa học nào khớp "{keyword}".', { keyword }))}</div>` : ''}
            <button type="button" class="ts-item ts-all" role="option" data-all="1">
                <i class="ph ph-list-magnifying-glass"></i>
                <span class="ts-text">${esc(t('search.xemTatCaKhoaHoc', 'Tìm "{keyword}" trong danh sách khóa học', { keyword }))}</span>
                <kbd class="ts-kbd">Enter</kbd>
            </button>`;

        itemList = [...box.querySelectorAll('.ts-item')].map(el => ({
            el,
            run: el.dataset.person ? () => { close(); openPersonCard(+el.dataset.person); }
                : el.dataset.course ? () => {
                    const course = courseList.find(c => c.id === +el.dataset.course);
                    close();
                    courseActionOf(course).run();
                }
                : goCourseList
        }));
        // Chưa chọn dòng nào: Enter giữ hành vi cũ (sang danh sách khóa học).
        activeIndex = -1;
        open();
    }

    async function search() {
        const keyword = input.value.trim();
        const mySeq = ++seq;
        if (keyword.length < 2) { close(); return; }
        let result;
        try { result = await API.getSilent(`/search?keyword=${encodeURIComponent(keyword)}`); }
        catch { result = null; }
        // Gõ tiếp trong lúc chờ thì kết quả về muộn này đã lỗi thời.
        if (mySeq !== seq || document.activeElement !== input) return;
        render(keyword, result);
    }

    input.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(search, 220);
    });
    input.addEventListener('focus', () => { if (input.value.trim().length >= 2) search(); });

    // mousedown chứ không phải click: click đến SAU khi ô tìm kiếm mất tiêu điểm, lúc đó ô gợi ý đã bị đóng.
    box.addEventListener('mousedown', (e) => {
        const item = e.target.closest('.ts-item');
        if (!item) return;
        e.preventDefault();
        itemList.find(x => x.el === item)?.run();
    });
    box.addEventListener('mousemove', (e) => {
        const index = itemList.findIndex(x => x.el === e.target.closest('.ts-item'));
        if (index >= 0 && index !== activeIndex) setActive(index);
    });
    input.addEventListener('blur', () => setTimeout(close, 120));

    return {
        isOpen: () => !box.hidden,
        close,
        move(step) {
            if (!itemList.length) return;
            setActive((activeIndex + step + itemList.length) % itemList.length);
        },
        pickActive() {
            if (box.hidden || activeIndex < 0 || !itemList[activeIndex]) return false;
            itemList[activeIndex].run();
            return true;
        }
    };
}

/** Việc làm được với một khóa học tìm thấy, tùy quan hệ của người xem với khóa đó. Dùng chung cho ô gợi ý lẫn thẻ
 *  hồ sơ để hai nơi luôn mời cùng một việc. @returns {{label: string, run: () => void, disabled?: boolean}} */
function courseActionOf(course, onEnrolled) {
    if (course.enrollmentId && course.enrollmentStatus !== 'Cancelled')
        return {
            label: course.enrollmentStatus === 'Completed' ? t('search.xemLai', 'Xem lại') : t('search.vaoHoc', 'Vào học'),
            run: () => { location.href = `/pages/scorm-player.html?enrollmentId=${course.enrollmentId}`; }
        };
    if (course.isMine)
        return { label: t('search.moKhoaHoc', 'Mở khóa học'), run: () => { location.href = `/pages/course-detail.html?id=${course.id}`; } };
    // Lượt học đã bị người phụ trách gỡ thì không mời tự đăng ký lại — máy chủ cũng sẽ từ chối.
    if (course.enrollmentStatus === 'Cancelled')
        return { label: t('search.daGoPhanCong', 'Đã gỡ phân công'), disabled: true, run: () => {} };
    return {
        label: t('suggest.enroll', 'Đăng ký học'),
        run: async () => { if (await selfEnrollCourse(course.id, course.title)) onEnrolled?.(); }
    };
}

/** Thẻ hồ sơ một người (mở từ ô tìm kiếm): thông tin trong hệ thống + các khóa học họ soạn mà mình được thấy.
 *  Hộp thoại dựng một lần rồi dùng lại, nên mở được từ bất kỳ trang nào có thanh trên. */
async function openPersonCard(userId) {
    if (!document.getElementById('person-modal')) {
        document.body.insertAdjacentHTML('beforeend', `
            <div class="modal-mask" id="person-modal">
                <div class="modal wide pc-modal" role="dialog" aria-modal="true" aria-labelledby="pc-name">
                    <button class="close-x pc-close" type="button" aria-label="${esc(t('common.close', 'Đóng'))}"
                            onclick="closeModal('person-modal')">×</button>
                    <div class="pc-body" id="pc-body"></div>
                </div>
            </div>`);
    }
    const body = document.getElementById('pc-body');
    body.innerHTML = `<div style="padding:24px">${skeletonHtml('list', { count: 3 })}</div>`;
    openModal('person-modal');

    let person;
    try { person = await API.getSilent(`/search/person/${userId}`); }
    catch (ex) { body.innerHTML = `<div style="padding:24px">${emptyHtml(ex.message, '<i class="ph ph-warning"></i>')}</div>`; return; }

    const avatarHtml = person.avatarUrl
        ? `<img class="avatar pc-avatar" src="${esc(person.avatarUrl)}" alt="">`
        : `<span class="avatar pc-avatar">${esc(initial(person.fullName))}</span>`;
    const coverStyle = person.coverUrl
        ? `style="background:center / cover no-repeat url('${encodeURI(person.coverUrl).replace(/'/g, '%27')}')"` : '';
    const fact = (icon, label, value) => value ? `
        <div class="pc-fact"><span class="pc-fact-icon"><i class="ph ${icon}"></i></span><span><small>${esc(label)}</small><b>${value}</b></span></div>` : '';
    const isMe = person.id === Auth.profile?.id;

    body.innerHTML = `
        <div class="pc-cover" ${coverStyle}></div>
        <div class="pc-head">
            ${avatarHtml}
            <div class="pc-title">
                <h3 id="pc-name">${esc(person.fullName)}${isMe ? ` <span class="badge gray">${esc(t('search.laBan', 'Bạn'))}</span>` : ''}</h3>
                <div class="pc-sub"><span class="badge blue">${esc(person.roleName)}</span> <span class="pc-code">${esc(person.employeeCode)}</span></div>
            </div>
        </div>
        <div class="pc-facts">
            ${fact('ph-buildings', t('search.phongBan', 'Phòng ban'), esc(person.departmentName || '—'))}
            ${fact('ph-identification-badge', t('search.viTri', 'Vị trí'), esc(person.positionName || '—'))}
            ${fact('ph-envelope-simple', 'Email', `<a href="mailto:${esc(person.email)}">${esc(person.email)}</a>`)}
            ${fact('ph-seal-check', t('search.chungNhan', 'Chứng nhận'), esc(String(person.certificateCount)))}
            ${fact('ph-calendar-blank', t('search.thamGiaTu', 'Tham gia từ'), esc(formatDate(person.createdAt)))}
        </div>
        <div class="pc-courses">
            <h4>${esc(t('search.khoaHocDoSoan', 'Khóa học do {name} soạn', { name: person.fullName }))}
                <span class="badge gray">${person.authoredCourseList.length}</span></h4>
            <p class="pc-note">${esc(t('search.khoaHocDoSoanNote', 'Chỉ hiện khóa học dành cho toàn công ty và khóa học dành cho phòng ban / vị trí của bạn.'))}</p>
            ${person.authoredCourseList.length ? `<div class="pc-course-list">${person.authoredCourseList.map(c => {
                const action = courseActionOf(c, () => openPersonCard(userId));
                return `
                    <div class="pc-course">
                        ${courseThumbHtml(c.title, c.thumbnailUrl, 'pc-thumb')}
                        <div class="pc-course-text">
                            <b>${esc(c.title)}</b>
                            <div class="pc-course-meta">
                                <span class="code">${esc(c.code)}</span>
                                ${c.audience === 'Company'
                                    ? `<span class="badge green">${esc(t('search.toanCongTy', 'Toàn công ty'))}</span>`
                                    : `<span class="badge orange">${esc(c.isMine ? t('search.theoPhongBan', 'Theo phòng ban') : t('search.choPhongBanCuaBan', 'Dành cho phòng ban của bạn'))}</span>`}
                                ${c.isMandatory ? `<span class="badge red">${esc(t('suggest.mandatory', 'Bắt buộc'))}</span>` : ''}
                                <span><i class="ph ph-clock"></i> ${formatDuration(c.durationMinutes)}</span>
                                <span><i class="ph ph-list-dashes"></i> ${esc(t('my-course.soBaiHoc', '{n} bài học', { n: c.lessonCount }))}</span>
                            </div>
                        </div>
                        <button type="button" class="btn sm ${action.disabled ? 'ghost' : ''}" data-pc-course="${c.id}" ${action.disabled ? 'disabled' : ''}>${esc(action.label)}</button>
                    </div>`;
            }).join('')}</div>`
            : emptyHtml(t('search.chuaCoKhoaHoc', 'Chưa có khóa học nào bạn xem được.'), '<i class="ph ph-books"></i>')}
        </div>`;

    body.querySelectorAll('[data-pc-course]').forEach(button => button.addEventListener('click', () => {
        const course = person.authoredCourseList.find(c => c.id === +button.dataset.pcCourse);
        courseActionOf(course, () => openPersonCard(userId)).run();
    }));
}

/** Phần "vỏ" của thanh trên: liên kết bỏ qua tới nội dung, và bóng đổ chỉ hiện khi trang đã cuộn — đứng yên ở
 *  đầu trang thì thanh trên hòa vào nền, cuộn xuống thì nó nổi lên tách khỏi nội dung đang trôi bên dưới. */
function mountTopbarChrome() {
    document.getElementById('skip-to-content')?.addEventListener('click', (e) => {
        e.preventDefault();
        const target = document.querySelector('.main .content') || document.querySelector('.main');
        if (!target) return;
        if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
        target.focus();
    });

    const topbar = document.getElementById('topbar');
    if (!topbar) return;
    const syncShadow = () => topbar.classList.toggle('is-scrolled', window.scrollY > 4);
    window.addEventListener('scroll', syncShadow, { passive: true });
    syncShadow();
}

// ==================== CHỈ SỐ HỌC TẬP (chuỗi ngày, điểm, kinh nghiệm) ====================
/**
 * Đổ chuỗi ngày học, điểm tích lũy và thanh kinh nghiệm vào vỏ trang.
 *
 * Chạy nền sau khi vỏ đã dựng, và im lặng bỏ qua khi lỗi: đây là thông tin trang trí thêm, không đáng để
 * một API chậm hay một vai trò không có quyền xếp hạng làm hỏng cả trang.
 */
async function mountRankBadge() {
    const [status, dashboard] = await Promise.all([
        API.getSilent('/ranking/me').catch(() => null),
        API.getSilent('/dashboard/learner').catch(() => null),
    ]);

    if (dashboard) {
        setPill('pill-streak', 'pill-streak-value', dashboard.streakDay);
        setPill('pill-point', 'pill-point-value', dashboard.totalPoint);
    }

    if (!status) return;

    // Cấp bậc hiện tại thay cho dòng "vai trò · phòng ban" nếu người này đã có hạng — với người học thì
    // cấp bậc là thứ họ quan tâm hơn tên vai trò.
    const subEl = document.getElementById('sidebar-user-sub');
    if (subEl && status.currentTier) subEl.textContent = status.currentTier.name;

    // Đã ở hạng cao nhất thì không còn mốc nào để tiến tới — hiện tổng điểm thay vì một thanh đầy 100%
    // kèm mẫu số bịa ra.
    const box = document.getElementById('sidebar-xp');
    const textEl = document.getElementById('sidebar-xp-text');
    const fillEl = document.getElementById('sidebar-xp-fill');
    if (!box || !textEl || !fillEl) return;

    // Không hạng hiện tại LẪN không mốc kế tiếp = người này không tham gia xếp hạng (Admin — xem
    // RankingService.GetStatusAsync). Để nguyên thì thanh vẽ đầy 100% y như người đã lên hạng cao nhất.
    // Người học mới toanh không rơi vào đây: họ chưa có hạng nhưng vẫn có mốc kế tiếp để tiến tới.
    if (!status.currentTier && !status.nextTier) return;

    textEl.textContent = status.nextTier
        ? `${status.totalPoints}/${status.nextTier.minPoints} XP`
        : `${status.totalPoints} XP`;
    fillEl.style.width = `${status.nextTier ? Math.min(100, Number(status.progressPercent) || 0) : 100}%`;
    box.classList.remove('hidden');
}

/** Hiện một viên chỉ số trên thanh trên. Giá trị 0 vẫn hiện: "chuỗi 0 ngày" là một thông tin thật, ẩn đi
 *  thì người dùng tưởng tính năng bị lỗi. */
function setPill(boxId, valueId, value) {
    const box = document.getElementById(boxId);
    if (!box) return;
    document.getElementById(valueId).textContent = Number(value ?? 0).toLocaleString(Lang.locale);
    box.classList.remove('hidden');
}

// ==================== CHẾ ĐỘ SÁNG / TỐI ====================
// Đã chuyển sang js/theme.js — trang đăng nhập không nạp app.js nhưng vẫn cần nút chuyển nền.
// Mọi trang nạp app.js đều nạp theme.js ngay trước đó, nên Theme / renderThemeSwitch() vẫn dùng được ở đây.

// ==================== NHẬP LIỆU TỪ EXCEL (dùng chung) ====================
// Ba màn hình đang dùng: Người dùng, Phòng ban, Vị trí công việc — và cả phần nhập bài học / câu hỏi trong
// khóa học cũng theo đúng khuôn kết quả này. Để mỗi trang tự viết lấy thì bảng lỗi mỗi nơi một kiểu, mà đây
// lại chính là thứ người dùng phải đọc kỹ nhất khi file có dòng sai.

/** Hộp thoại kết quả, tự chèn vào trang một lần. */
function ensureImportModal() {
    if (document.getElementById('modal-import-result')) return;

    document.body.insertAdjacentHTML('beforeend', `
        <div class="modal-mask" id="modal-import-result">
            <div class="modal">
                <div class="modal-head">
                    <h3>${esc(t('import.ketQua', 'Kết quả nhập liệu từ Excel'))}</h3>
                    <button class="close-x" type="button" onclick="closeModal('modal-import-result')">×</button>
                </div>
                <div class="modal-body" id="import-result-body"></div>
                <div class="modal-foot">
                    <button class="btn" type="button" onclick="closeModal('modal-import-result')">${esc(t('common.close', 'Đóng'))}</button>
                </div>
            </div>
        </div>`);
}

/** Hiện bảng tổng kết + danh sách dòng bị bỏ qua kèm lý do. */
function showImportResult(result) {
    ensureImportModal();

    document.getElementById('import-result-body').innerHTML = `
        <div class="grid grid-3 mb">
            <div class="stat"><div class="label">${esc(t('import.tongSoDong', 'Tổng số dòng'))}</div><div class="value">${result.totalRow}</div></div>
            <div class="stat ok"><div class="label">${esc(t('import.nhapThanhCong', 'Nhập thành công'))}</div><div class="value">${result.successCount}</div></div>
            <div class="stat ${result.failCount ? 'bad' : ''}"><div class="label">${esc(t('import.biBoQua', 'Bị bỏ qua'))}</div><div class="value">${result.failCount}</div></div>
        </div>
        ${result.errorList.length ? `
            <div class="small muted mb">${esc(t('import.cacDongBiBoQua', 'Các dòng dưới đây bị bỏ qua, những dòng còn lại vẫn được nhập bình thường:'))}</div>
            <div class="table-wrap" style="max-height:280px;overflow:auto">
                <table><thead><tr>
                    <th style="width:80px">${esc(t('import.dong', 'Dòng'))}</th>
                    <th>${esc(t('import.lyDo', 'Lý do'))}</th>
                </tr></thead>
                <tbody>${result.errorList.map(e => `<tr><td><b>${e.rowNumber}</b></td><td>${esc(e.message)}</td></tr>`).join('')}</tbody>
                </table>
            </div>`
        : `<div class="empty"><div class="big"><i class="ph ph-check-circle"></i></div>${esc(t('import.tatCaThanhCong', 'Toàn bộ dữ liệu đã được nhập thành công.'))}</div>`}`;

    openModal('modal-import-result');
    if (result.failCount === 0) toastOk(result.message); else toastErr(result.message);
}

/**
 * Tải file lên rồi hiện kết quả. Trong lúc tải, chính nút bấm biến thành thanh tiến độ và bị khóa lại —
 * file vài nghìn dòng mất một lúc, không có phản hồi thì người dùng bấm lại lần nữa và nhập trùng.
 *
 * @param {string} path đường dẫn API, VD "/user/import"
 * @param {File} file tệp Excel người dùng vừa chọn
 * @param {HTMLElement} buttonEl nút Import để hiện tiến độ
 * @param {Function} onDone gọi lại sau khi nhập xong để trang tự nạp lại danh sách
 */
async function runExcelImport(path, file, buttonEl, onDone) {
    const originalLabel = buttonEl?.innerHTML;
    if (buttonEl) {
        buttonEl.disabled = true;
        buttonEl.innerHTML = `<span class="spinner-inline"></span> ${esc(t('import.dangTaiLen', 'Đang tải lên'))} 0%`;
    }

    try {
        const result = await API.uploadWithProgress(path, file, (percent) => {
            if (!buttonEl) return;
            buttonEl.innerHTML = percent < 100
                ? `<span class="spinner-inline"></span> ${esc(t('import.dangTaiLen', 'Đang tải lên'))} ${percent}%`
                : `<span class="spinner-inline"></span> ${esc(t('import.dangXuLy', 'Đang xử lý...'))}`;
        });
        showImportResult(result);
        await onDone?.();
    } catch (ex) {
        toastErr(ex.message);
    } finally {
        if (buttonEl) { buttonEl.disabled = false; buttonEl.innerHTML = originalLabel; }
    }
}

/**
 * Gắn cặp nút "Tải file mẫu" + "Import Excel" đã có sẵn trong trang.
 * @param {object} option { templateId, importId, fileId, templatePath, importPath, fileName, onDone }
 *        templatePath/importPath nhận được là HÀM để trang nào có nhiều tab (VD Phòng ban / Vị trí) trả về
 *        đường dẫn theo tab đang mở tại thời điểm bấm, thay vì cố định lúc gắn sự kiện.
 */
function bindExcelImport(option) {
    const templateBtn = document.getElementById(option.templateId);
    const importBtn = document.getElementById(option.importId);
    const fileInput = document.getElementById(option.fileId);
    if (!importBtn || !fileInput) return;

    const resolve = (value) => (typeof value === 'function' ? value() : value);

    templateBtn?.addEventListener('click', () =>
        API.download(resolve(option.templatePath), resolve(option.fileName)).catch(ex => toastErr(ex.message)));

    importBtn.addEventListener('click', () => fileInput.click());

    fileInput.addEventListener('change', (e) => {
        const file = e.target.files[0];
        // Xóa lựa chọn ngay: chọn lại ĐÚNG tệp vừa rồi (sau khi sửa lỗi trong Excel) vẫn phải kích hoạt lại.
        e.target.value = '';
        if (file) runExcelImport(resolve(option.importPath), file, importBtn, option.onDone);
    });
}

// ==================== CHUÔNG THÔNG BÁO ====================
/**
 * Chuông thông báo: huy hiệu số chưa đọc trên thanh trên + bảng thông báo xổ xuống.
 *
 * Bảng được nhóm theo NGÀY rồi trong mỗi ngày gộp tiếp các thông báo CÙNG LOẠI liền nhau thành một thẻ.
 * Không gộp thì một đợt phân công 5 khóa học sinh ra 5 thẻ chữ y hệt nhau chiếm trọn màn hình, người dùng
 * phải cuộn qua hết mới thấy được thông báo khác loại nằm dưới.
 */
function mountNotificationBell() {
    const bell = document.getElementById('notif-bell');
    const btn = document.getElementById('notif-bell-btn');
    const dropdown = document.getElementById('notif-dropdown');
    const badge = document.getElementById('notif-badge');
    const list = document.getElementById('notif-list');
    if (!bell) return;

    /** Số thông báo hiện tối đa lúc mới mở. Nút "Xem tất cả" ở chân bảng mở nốt phần còn lại. */
    const PREVIEW_COUNT = 6;

    let itemList = [];
    let filterUnread = false;
    let showAll = false;

    const setBadge = (count) => {
        badge.textContent = count > 99 ? '99+' : count;
        badge.classList.toggle('hidden', !count);
        const newBadge = document.getElementById('notif-new-badge');
        if (newBadge) {
            newBadge.textContent = t('shell.notifNew', '{count} mới', { count });
            newBadge.classList.toggle('hidden', !count);
        }
    };

    /** Số chưa đọc của lần đếm TRƯỚC. Tăng lên giữa hai nhịp nghĩa là vừa có thông báo mới tới nơi trong lúc
     *  người dùng đang mở trang — đó là lúc bật cửa sổ thông báo của trình duyệt, để lời nhắc đã hẹn giờ tới
     *  được mắt người học ngay cả khi thẻ này đang nằm sau lưng cửa sổ khác. null = chưa đếm lần nào (lần đầu
     *  chỉ ghi nhận con số, không bung thông báo cho những cái đã tồn tại từ trước).  */
    let lastUnreadCount = null;

    async function refreshCount() {
        try {
            const { count } = await API.getSilent('/notification/unread-count');
            setBadge(count);
            if (lastUnreadCount !== null && count > lastUnreadCount) await popNewNotification();
            lastUnreadCount = count;
        }
        catch { /* im lặng bỏ qua — không chặn tải trang vì lỗi thông báo */ }
    }

    /** Bung thông báo cho những cái vừa tới mà chưa từng bung. Chỉ gọi khi số chưa đọc TĂNG, nên không phải
     *  nhịp 60 giây nào cũng kéo cả danh sách về. */
    async function popNewNotification() {
        let freshList;
        try { freshList = await API.getSilent('/notification'); }
        catch { return; }

        const poppedIdList = readPoppedIdList();
        const newList = freshList.filter(x => !x.isRead && !poppedIdList.includes(x.id));
        if (!newList.length) return;

        // Nhiều cái cùng lúc thì chỉ bung ba cái mới nhất — chồng chục cửa sổ thông báo lên màn hình
        // là cách nhanh nhất để người dùng tắt quyền thông báo vĩnh viễn.
        newList.slice(0, 3).forEach(showDesktopNotification);
        writePoppedIdList(poppedIdList.concat(newList.map(x => x.id)));
    }

    function showDesktopNotification(item) {
        const link = linkOf(item);

        // Chưa được cấp quyền (hoặc trình duyệt không có Notification) thì lui về thông báo nổi trong trang —
        // vẫn thấy được nếu người dùng đang nhìn vào tab này.
        if (typeof Notification === 'undefined' || Notification.permission !== 'granted') {
            toast(`${item.title} — ${item.message}`);
            return;
        }

        try {
            const popup = new Notification(item.title, {
                body: item.message,
                icon: '/img/icons/icon-192.png',
                // tag theo id: gọi lại đúng thông báo đó (VD mở hai tab) chỉ thay chỗ cũ, không đẻ thêm cái mới.
                tag: `hbe-notification-${item.id}`
            });
            popup.onclick = () => {
                window.focus();
                if (link) location.href = link;
                popup.close();
            };
        } catch { toast(`${item.title} — ${item.message}`); }
    }

    /** Id các thông báo đã từng bung, giữ trong localStorage để chuyển trang không bung lại từ đầu.
     *  Chỉ giữ 100 cái gần nhất — đủ xa để không lặp, mà không phình mãi. */
    function readPoppedIdList() {
        try { return JSON.parse(localStorage.getItem('hbe_notification_popped') || '[]'); }
        catch { return []; }
    }
    function writePoppedIdList(idList) {
        try { localStorage.setItem('hbe_notification_popped', JSON.stringify(idList.slice(-100))); }
        catch { /* hết chỗ lưu / chế độ riêng tư — cùng lắm là bung lại một lần, không đáng để hỏng gì */ }
    }

    /** Icon và tông màu của từng loại thông báo. Màu nói trước nội dung: đỏ là việc trễ, vàng là sắp tới
     *  hạn, xanh lá là đã xong, còn lại dùng màu chủ đạo. */
    function notifStyle(type) {
        return {
            CourseAssigned: ['ph-book-bookmark', ''],
            NotStarted: ['ph-alarm', 'warn'],
            DueSoon: ['ph-hourglass', 'warn'],
            Overdue: ['ph-warning', 'danger'],
            RankUp: ['ph-trophy', 'gold'],
            CourseSubmittedForApproval: ['ph-paper-plane-tilt', ''],
            CourseApproved: ['ph-check-circle', 'success'],
            CourseRejected: ['ph-x-circle', 'danger'],
            CoursePublished: ['ph-megaphone', ''],
            WeeklyReminder: ['ph-calendar-check', 'warn'],
            StudyReminder: ['ph-alarm', ''],
            ShopRedemption: ['ph-gift', 'gold'],
            ExternalCertificate: ['ph-certificate', ''],
            CourseFeedback: ['ph-chat-circle-text', ''],
        }[type] || ['ph-bell', ''];
    }

    /** Nhãn của nút hành động, theo việc mà thông báo đó dẫn tới. */
    function actionLabelOf(item) {
        if (item.type === 'CourseFeedback' && item.courseId) return t('feedback.xemPhanHoi', 'Xem phản hồi');
        if (item.enrollmentId) return t('shell.notifGoLearn', 'Học ngay');
        if (item.courseId) return t('shell.notifGoCourse', 'Xem khóa học');
        // Thông báo đổi quà không gắn với khóa học nào, nhưng vẫn có chỗ để dẫn tới: lịch sử đổi quà của
        // chính người nhận, nơi đọc được trạng thái từng lượt.
        if (item.type === 'ShopRedemption') return t('shell.notifGoShop', 'Xem lượt đổi');
        if (item.type === 'ExternalCertificate') return t('shell.notifGoCertificate', 'Xem chứng nhận');
        return null;
    }

    function linkOf(item) {
        // Người soạn khóa đọc ở thẻ Feedback của khóa; người học (nhận trả lời) về hộp phản hồi ở Khóa học của tôi.
        if (item.type === 'CourseFeedback' && item.courseId)
            return Auth.isContentAuthor || Auth.isAdmin
                ? `/pages/course-detail.html?id=${item.courseId}&tab=feedback`
                : `/pages/my-course.html?feedback=${item.courseId}`;
        if (item.enrollmentId) return `/pages/scorm-player.html?enrollmentId=${item.enrollmentId}`;
        if (item.courseId) return `/pages/course-detail.html?id=${item.courseId}`;
        if (item.type === 'ShopRedemption') return '/pages/shop.html?tab=mine';
        // Nhân viên nhận kết quả AI / Admin đối chiếu -> thẻ của mình; Admin (thông báo "chờ duyệt" cũ) -> thẻ quản lý.
        if (item.type === 'ExternalCertificate')
            return `/pages/certificate.html?tab=${Auth.isAdmin ? 'review' : 'external'}`;
        return null;
    }

    /** Nhãn ngày cho một mốc thời gian: HÔM NAY / HÔM QUA / ngày cụ thể. */
    function dayLabel(value) {
        const date = new Date(value);
        const today = new Date(); today.setHours(0, 0, 0, 0);
        const dayDiff = Math.round((today - new Date(date).setHours(0, 0, 0, 0)) / 86400000);
        if (dayDiff <= 0) return t('shell.notifToday', 'Hôm nay');
        if (dayDiff === 1) return t('shell.notifYesterday', 'Hôm qua');
        return formatDate(value);
    }

    /** Gom danh sách phẳng thành [{ day, cardList }], mỗi card là 1 thông báo hoặc 1 cụm cùng loại. */
    function groupItemList(source) {
        const dayGroupList = [];

        for (const item of source) {
            const day = dayLabel(item.createdAt);
            let dayGroup = dayGroupList.at(-1);
            if (!dayGroup || dayGroup.day !== day) {
                dayGroup = { day, cardList: [] };
                dayGroupList.push(dayGroup);
            }

            // Chỉ gộp thông báo LIỀN NHAU và cùng loại: gộp cả những cái cách xa nhau về thời gian thì dòng
            // "2 giờ trước" của cụm không còn đúng với thành viên nào trong cụm.
            const lastCard = dayGroup.cardList.at(-1);
            if (lastCard && lastCard.itemList[0].type === item.type && linkOf(item)) {
                lastCard.itemList.push(item);
            } else {
                dayGroup.cardList.push({ itemList: [item] });
            }
        }
        return dayGroupList;
    }

    function cardHtml(card) {
        const [first] = card.itemList;
        const [icon, tone] = notifStyle(first.type);
        const hasUnread = card.itemList.some(x => !x.isRead);
        const oldest = card.itemList.at(-1);

        // Cụm nhiều thông báo: tiêu đề chung + mỗi khóa học một dòng bấm được riêng.
        if (card.itemList.length > 1) {
            return `<div class="notif-card ${hasUnread ? 'unread' : ''}" data-group="1">
                <div class="notif-card-top">
                    <span class="notif-ico ${tone}"><i class="ph-fill ${icon}"></i></span>
                    <div class="notif-body">
                        <div class="notif-title"><span>${esc(first.title)}</span>
                            ${hasUnread ? '<i class="notif-dot"></i>' : ''}</div>
                        ${card.itemList.map(x => `
                            <div class="notif-mini" data-id="${x.id}" data-link="${esc(linkOf(x) || '')}">
                                <i class="mini-dot ${x.isRead ? 'read' : ''}"></i>
                                <span class="mini-name">${esc(x.courseTitle || x.message)}</span>
                                <span class="mini-go">${esc(actionLabelOf(x))}</span>
                            </div>`).join('')}
                        <div class="notif-foot"><span class="notif-time">${esc(timeAgo(oldest.createdAt))}</span></div>
                    </div>
                </div>
            </div>`;
        }

        const link = linkOf(first);
        const actionLabel = actionLabelOf(first);

        return `<div class="notif-card ${first.isRead ? '' : 'unread'}" data-id="${first.id}" data-link="${esc(link || '')}">
            <div class="notif-card-top">
                <span class="notif-ico ${tone}"><i class="ph-fill ${icon}"></i></span>
                <div class="notif-body">
                    <div class="notif-title"><span>${esc(first.title)}</span>
                        ${first.isRead ? '' : '<i class="notif-dot"></i>'}</div>
                    <div class="notif-msg">${esc(first.message)}</div>
                    ${first.courseTitle ? `<div class="notif-chip"><i class="ph ph-book-open"></i>
                        <span>${esc(first.courseTitle)}</span></div>` : ''}
                    <div class="notif-foot">
                        <span class="notif-time">${esc(timeAgo(first.createdAt))}</span>
                        ${actionLabel ? `<span class="notif-action">${esc(actionLabel)}
                            <i class="ph-bold ph-arrow-right"></i></span>` : ''}
                    </div>
                </div>
            </div>
        </div>`;
    }

    function render() {
        const unreadCount = itemList.filter(x => !x.isRead).length;

        // Hai viên lọc luôn hiện số thật, kể cả khi đang ở tab kia — người dùng biết bên kia còn bao nhiêu
        // mà không phải bấm sang xem.
        document.getElementById('notif-tab-all').classList.toggle('active', !filterUnread);
        document.getElementById('notif-tab-unread').classList.toggle('active', filterUnread);
        document.getElementById('notif-tab-unread-count').textContent = unreadCount;
        document.getElementById('notif-mark-all').disabled = unreadCount === 0;
        setBadge(unreadCount);

        const filteredList = filterUnread ? itemList.filter(x => !x.isRead) : itemList;
        const shownList = showAll ? filteredList : filteredList.slice(0, PREVIEW_COUNT);

        list.innerHTML = shownList.length
            ? groupItemList(shownList).map(group => `
                <div class="notif-day">${esc(group.day)}</div>
                ${group.cardList.map(cardHtml).join('')}`).join('')
            : `<div class="notif-empty"><i class="ph ph-bell-slash"></i>${esc(filterUnread
                ? t('shell.notifNoUnread', 'Bạn đã đọc hết thông báo.')
                : t('shell.khongCoThongBaoNao', 'Không có thông báo nào.'))}</div>`;

        const footer = document.getElementById('notif-footer');
        const remaining = filteredList.length - shownList.length;
        footer.classList.toggle('hidden', remaining <= 0);
        if (remaining > 0)
            footer.querySelector('button').innerHTML =
                `${esc(t('shell.notifSeeAll', 'Xem tất cả thông báo'))} (${remaining})
                 <i class="ph-bold ph-arrow-right"></i>`;

        bindCard();
    }

    /** Đánh dấu đã đọc rồi chuyển trang. Bấm vào thông báo nào cũng phải xóa được dấu chưa đọc của nó, kể
     *  cả khi nó không dẫn tới đâu. */
    async function openItem(id, link) {
        const item = itemList.find(x => x.id === Number(id));
        if (item && !item.isRead) {
            item.isRead = true;
            try { await API.postSilent(`/notification/${id}/read`); } catch { /* bỏ qua */ }
        }
        if (link) location.href = link;
        else render();
    }

    function bindCard() {
        // Dòng con trong cụm phải chặn nổi bọt, nếu không cú bấm chạy tiếp lên thẻ cha và mở nhầm khóa học.
        list.querySelectorAll('.notif-mini[data-id]').forEach(el => el.addEventListener('click', (e) => {
            e.stopPropagation();
            openItem(el.dataset.id, el.dataset.link);
        }));
        list.querySelectorAll('.notif-card[data-id]').forEach(el => el.addEventListener('click', () =>
            openItem(el.dataset.id, el.dataset.link)));
    }

    async function openDropdown() {
        dropdown.classList.remove('hidden');
        showAll = false;
        list.innerHTML = skeletonHtml('list', { count: 4, avatar: false });
        try {
            itemList = await API.get('/notification');
            render();
        } catch (ex) {
            list.innerHTML = `<div class="notif-empty"><i class="ph ph-warning"></i>${esc(ex.message)}</div>`;
        }
    }

    btn.addEventListener('click', (e) => {
        // Chặn ở đây thì cú bấm này không lọt xuống document để đóng ngay bảng vừa mở — nhưng cũng vì thế
        // mà nó không đóng hộ được bảng nào khác nữa, nên phải tự gọi closeTopbarOverlay.
        e.stopPropagation();
        const willOpen = dropdown.classList.contains('hidden');
        if (willOpen) closeTopbarOverlay('notif');
        dropdown.classList.toggle('hidden');
        if (willOpen) openDropdown();
    });

    // Bảng đang mở thì mọi cú bấm bên trong đều không được đóng nó — trừ các nút tự lo việc đóng/chuyển trang.
    dropdown.addEventListener('click', (e) => e.stopPropagation());

    document.getElementById('notif-tab-all').addEventListener('click', () => { filterUnread = false; showAll = false; render(); });
    document.getElementById('notif-tab-unread').addEventListener('click', () => { filterUnread = true; showAll = false; render(); });
    document.getElementById('notif-footer').querySelector('button')
        .addEventListener('click', () => { showAll = true; render(); });

    document.getElementById('notif-mark-all').addEventListener('click', async () => {
        try {
            await API.post('/notification/read-all');
            itemList.forEach(x => { x.isRead = true; });
            render();
        } catch (ex) { toastErr(ex.message); }
    });

    document.addEventListener('click', (e) => {
        if (!bell.contains(e.target)) dropdown.classList.add('hidden');
    });

    refreshCount();
    setInterval(refreshCount, 60000);
}

/** Trạng thái quyền hiện thông báo trên máy: 'granted' | 'denied' | 'default' | 'unsupported'. */
function desktopNotificationState() {
    return typeof Notification === 'undefined' ? 'unsupported' : Notification.permission;
}

/** Xin quyền hiện thông báo trên máy (cửa sổ nhỏ ở góc màn hình, hiện cả khi trình duyệt đang ở tab khác).
 *  PHẢI gọi từ một cú bấm của người dùng — trình duyệt bỏ qua lời xin quyền tự phát lúc tải trang.
 *  Trả về true nếu từ giờ hệ thống bung được thông báo lên máy họ. */
async function requestDesktopNotificationPermission() {
    if (typeof Notification === 'undefined') {
        toastErr('Trình duyệt này không hỗ trợ thông báo trên máy.');
        return false;
    }
    if (Notification.permission === 'granted') return true;
    if (Notification.permission === 'denied') {
        toastErr('Bạn đã chặn thông báo cho trang này. Hãy mở lại trong phần cài đặt quyền của trình duyệt.');
        return false;
    }

    const result = await Notification.requestPermission();
    if (result === 'granted') toastOk('Đã bật thông báo. Tới giờ đã hẹn, hệ thống sẽ nhắc bạn.');
    return result === 'granted';
}

/** Bật/tắt menu dạng overlay trên di động. */
function toggleMobileSidebar(force) {
    const sidebar = document.getElementById('sidebar');
    const backdrop = document.getElementById('sidebarBackdrop');
    if (!sidebar) return;
    const show = typeof force === 'boolean' ? force : !sidebar.classList.contains('open');
    sidebar.classList.toggle('open', show);
    backdrop?.classList.toggle('show', show);
    // Khóa cuộn phần thân trong lúc menu phủ lên (luật CSS gắn với class này, xem cuối css/style.css).
    // Thiếu bước này thì vuốt trên menu làm trang phía sau cuộn theo, đóng menu ra là đã lạc mất chỗ đang đọc.
    document.body.classList.toggle('sidebar-open', show);
}

// Xoay ngang máy hoặc mở ra màn hình rộng thì menu quay về dạng cột cố định — phải gỡ khóa cuộn, không thì
// trang trên máy tính bảng nằm đơ không cuộn được mà chẳng thấy menu nào đang mở.
window.addEventListener('resize', () => {
    if (window.innerWidth > 900) toggleMobileSidebar(false);
});

/** Nạp thư viện SignalR + hộp chat. Nạp động để không phải thêm 2 thẻ <script> vào cả 25 trang. */
function mountChatWidget() {
    if (!Auth.isLoggedIn || document.getElementById('chat-widget')) return;

    // onerror PHẢI resolve chứ không được bỏ trống: chỉ gắn onload thì một file tải hỏng (mạng chập
    // chờn, tệp bị đổi tên) sẽ treo chuỗi promise vĩnh viễn — hộp chat không dựng, mà console cũng
    // chẳng có lỗi nào để lần ra. Báo hỏng rồi đi tiếp, để chỗ gọi tự quyết định.
    const loadScript = (src, id) => new Promise(resolve => {
        if (document.getElementById(id)) return resolve(true);
        const s = document.createElement('script');
        s.id = id;
        s.src = src;
        s.onload = () => resolve(true);
        s.onerror = () => { s.remove(); resolve(false); };   // gỡ thẻ hỏng để lần vào trang sau còn thử lại
        document.body.appendChild(s);
    });

    loadScript('/js/lib/signalr.min.js', 'signalr-lib')
        .then(ok => ok ? loadScript('/js/chat.js', 'chat-script') : false)
        .then(ok => {
            if (!ok) { console.warn('Không nạp được hộp chat (js/chat.js hoặc signalr.min.js).'); return; }

            // Gọi ChatBox TRẦN, không phải window.ChatBox: chat.js khai báo `const ChatBox = ...` ở cấp
            // cao nhất của một classic script, mà `const`/`let` cấp cao nhất nằm trong global lexical
            // environment chứ KHÔNG trở thành thuộc tính của window (chỉ `var` và function mới thế).
            // window.ChatBox vì vậy vĩnh viễn undefined — viết `window.ChatBox?.init()` thì dấu ?. nuốt
            // luôn, không dựng hộp chat mà console cũng sạch bong. Auth trong api.js cũng đúng kiểu này,
            // và cả dự án vẫn gọi thẳng `Auth.xxx` — giữ cho nhất quán.
            if (typeof ChatBox === 'undefined') { console.warn('js/chat.js không khai báo ChatBox.'); return; }
            ChatBox.init();
        });
}


// ==================== Ô CHỌN CÓ TÌM KIẾM ====================
/**
 * Biến một <select> dài thành ô chọn GÕ ĐƯỢC: bấm vào là hiện ô tìm kiếm, gõ vài chữ để lọc, Enter để chọn.
 *
 * Vì sao cần: danh sách phòng ban có gần trăm dòng. Danh sách xổ xuống mặc định của trình duyệt không có ô
 * tìm kiếm, nên muốn chọn "Phòng Đầu Tư" phải cuộn qua mấy chục dòng mới thấy — mỗi lần thêm một bước duyệt
 * là một lần cuộn như vậy.
 *
 * Bọc quanh <select> chứ KHÔNG thay thế nó: thẻ select gốc vẫn nằm trong DOM và vẫn là nơi giữ giá trị, nên
 * mọi đoạn mã đang đọc .value / .disabled / gửi kèm form đều chạy y nguyên, và mọi thay đổi ở đây vẫn phát
 * sự kiện 'change' như thường. Chỉ phần NHÌN THẤY được thay.
 *
 * Gọi lại trên cùng một select là AN TOÀN và cần thiết: biểu mẫu thường dựng lại toàn bộ <option> rồi gán
 * .value / .disabled mới (xem renderWorkflowField ở course.html), lúc đó gọi lại để nhãn hiển thị bám theo.
 *
 * @param {HTMLSelectElement} select ô chọn đã có sẵn trong DOM.
 * @param {{placeholder?: string}} [options] chữ gợi ý trong ô tìm kiếm.
 */
function enhanceSearchableSelect(select, options = {}) {
    if (!select) return;

    // Đã bọc rồi thì chỉ đồng bộ lại nhãn/trạng thái theo <option> và .value hiện tại.
    if (select._ssSync) { select._ssSync(); return; }

    const wrap = document.createElement('div');
    wrap.className = 'ss-wrap';
    select.parentNode.insertBefore(wrap, select);
    wrap.appendChild(select);
    select.classList.add('ss-native');
    // Ô gốc bị ẩn nên không nhận được tiêu điểm — thẻ nào đang trỏ tới nó bằng <label for> phải chuyển sang
    // nút thay thế, nếu không bấm vào nhãn sẽ không mở được danh sách.
    select.tabIndex = -1;
    // Ẩn bằng opacity thì trình đọc màn hình vẫn đọc đủ cả trăm <option> bên trong, và innerText của cả khối
    // cũng dính theo. Nhận diện đã chuyển sang nút thay thế (mang aria-label và role listbox), nên ẩn hẳn ô gốc
    // khỏi cây trợ năng — an toàn vì nó đã không còn nhận tiêu điểm ở trên.
    select.setAttribute('aria-hidden', 'true');

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'ss-button';
    button.innerHTML = '<span></span>';
    if (select.id) {
        button.id = select.id + '-ss';
        document.querySelectorAll(`label[for="${select.id}"]`).forEach(l => l.setAttribute('for', button.id));
    }
    const ariaLabel = select.getAttribute('aria-label');
    if (ariaLabel) button.setAttribute('aria-label', ariaLabel);
    wrap.appendChild(button);

    const panel = document.createElement('div');
    panel.className = 'ss-panel';
    panel.innerHTML = `
        <input type="text" class="ss-search" autocomplete="off" spellcheck="false"
               placeholder="${esc(options.placeholder || t('shell.goDeTim', 'Gõ để tìm...'))}">
        <div class="ss-list" role="listbox"></div>`;
    // Gắn vào <body> chứ không vào wrap — xem chú thích .ss-panel trong style.css.
    document.body.appendChild(panel);
    searchableSelectList.push({ wrap, panel });

    const search = panel.querySelector('.ss-search');
    const list = panel.querySelector('.ss-list');
    let matchList = [], activeIndex = -1;

    const syncButton = () => {
        const option = select.selectedOptions[0];
        button.firstElementChild.textContent = option ? option.textContent.trim() : '—';
        button.disabled = select.disabled;
        // Dòng gợi ý ("— Chọn phòng ban —") tô nhạt như placeholder để phân biệt với một lựa chọn thật.
        button.classList.toggle('placeholder', !option || option.value === '');
    };
    select._ssSync = syncButton;

    const render = () => {
        const keyword = normalizeForSearch(search.value.trim());
        matchList = [...select.options].filter(o => !keyword || normalizeForSearch(o.textContent).includes(keyword));

        list.innerHTML = matchList.length
            ? matchList.map((o, i) => {
                // Tên nhóm <optgroup> in nhỏ phía trên mục đầu tiên của nhóm — không có nó thì lọc xong
                // 96 phòng ban trộn lẫn với mấy dòng gợi ý ở đầu, không biết dòng nào thuộc đâu.
                const group = o.parentElement.tagName === 'OPTGROUP' ? o.parentElement.label : null;
                const previousGroup = i > 0 && matchList[i - 1].parentElement.tagName === 'OPTGROUP'
                    ? matchList[i - 1].parentElement.label : null;
                const head = group && group !== previousGroup ? `<div class="ss-group">${esc(group)}</div>` : '';
                return `${head}<button type="button" class="ss-item${o.value === select.value ? ' selected' : ''}"
                            role="option" data-index="${i}">${esc(o.textContent.trim())}</button>`;
            }).join('')
            : `<div class="ss-empty">${esc(t('shell.khongTimThayKetQua', 'Không tìm thấy kết quả nào.'))}</div>`;

        activeIndex = matchList.findIndex(o => o.value === select.value);
        if (activeIndex < 0 && matchList.length) activeIndex = 0;
        highlight(false);
    };

    const highlight = (scroll = true) => {
        list.querySelectorAll('.ss-item').forEach(el =>
            el.classList.toggle('active', +el.dataset.index === activeIndex));
        if (!scroll) return;
        list.querySelector('.ss-item.active')?.scrollIntoView({ block: 'nearest' });
    };

    const close = () => {
        wrap.classList.remove('open');
        panel.classList.remove('open');
        button.setAttribute('aria-expanded', 'false');
    };

    const open = () => {
        if (select.disabled) return;
        closeAllSearchableSelect();
        wrap.classList.add('open');
        panel.classList.add('open');
        button.setAttribute('aria-expanded', 'true');
        search.value = '';
        render();

        // Đo chỗ trống thật trên màn hình rồi mới quyết định mở xuống hay mở lên, và cho danh sách cao tối đa
        // bằng đúng chỗ còn lại — ô chọn nằm gần đáy hộp thoại thì thả xuống sẽ tràn ra ngoài màn hình.
        const box = button.getBoundingClientRect();
        const below = window.innerHeight - box.bottom - 12;
        const above = box.top - 12;
        const dropUp = below < 220 && above > below;
        const space = Math.max(150, Math.min(320, dropUp ? above : below));

        list.style.maxHeight = Math.max(80, space - 58) + 'px';
        panel.style.left = box.left + 'px';
        panel.style.width = box.width + 'px';
        if (dropUp) {
            panel.style.top = 'auto';
            panel.style.bottom = (window.innerHeight - box.top + 4) + 'px';
        } else {
            panel.style.bottom = 'auto';
            panel.style.top = (box.bottom + 4) + 'px';
        }

        search.focus();
    };

    const pick = (option) => {
        if (!option) return;
        select.value = option.value;
        syncButton();
        close();
        button.focus();
        // Phát sự kiện như người dùng vừa đổi ô chọn thật — các trình xử lý change/onchange đang gắn sẵn
        // (VD phần xem trước các bước duyệt ở biểu mẫu khóa học) nhờ đó vẫn chạy.
        select.dispatchEvent(new Event('change', { bubbles: true }));
    };

    button.setAttribute('aria-haspopup', 'listbox');
    button.setAttribute('aria-expanded', 'false');
    button.addEventListener('click', (e) => {
        e.stopPropagation();
        wrap.classList.contains('open') ? close() : open();
    });
    button.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); }
    });

    search.addEventListener('input', render);
    search.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            if (!matchList.length) return;
            activeIndex = (activeIndex + (e.key === 'ArrowDown' ? 1 : -1) + matchList.length) % matchList.length;
            highlight();
        } else if (e.key === 'Enter') {
            e.preventDefault();
            pick(matchList[activeIndex]);
        } else if (e.key === 'Escape') {
            e.preventDefault();
            close();
            button.focus();
        }
    });

    list.addEventListener('click', (e) => {
        const item = e.target.closest('.ss-item');
        if (item) pick(matchList[+item.dataset.index]);
    });

    // Bấm ra ngoài thì đóng. Chặn lan ở trong panel để bấm vào ô tìm kiếm không tự đóng chính nó; việc
    // đóng do MỘT trình xử lý dùng chung ở cuối tệp lo (xem closeAllSearchableSelect) — gắn riêng cho từng ô thì
    // mỗi cú bấm phải chạy qua hàng chục trình xử lý, mà vẫn không ai dọn được bảng chọn mồ côi.
    panel.addEventListener('click', (e) => e.stopPropagation());
    panel.addEventListener('mousedown', (e) => e.stopPropagation());

    syncButton();
}

/** Bỏ dấu tiếng Việt để gõ "dau tu" vẫn tìm ra "Phòng Đầu Tư". */
function normalizeForSearch(text) {
    return String(text).toLowerCase()
        .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
        .replace(/đ/g, 'd');
}

/** Mọi ô chọn đã dựng, để còn đóng hết và dọn bảng chọn mồ côi — bảng nằm ở <body> nên không biến mất
 *  theo khi ô chọn của nó bị gỡ khỏi trang (VD xóa một bước duyệt). */
const searchableSelectList = [];

/** Đóng mọi ô chọn đang mở — chỉ một cái được mở tại một thời điểm. */
function closeAllSearchableSelect() {
    for (let i = searchableSelectList.length - 1; i >= 0; i--) {
        const { wrap, panel } = searchableSelectList[i];
        if (!wrap.isConnected) {
            panel.remove();
            searchableSelectList.splice(i, 1);
            continue;
        }
        wrap.classList.remove('open');
        panel.classList.remove('open');
        wrap.querySelector('.ss-button')?.setAttribute('aria-expanded', 'false');
    }
}

// Bảng chọn được định vị một lần lúc mở, không bám theo ô chọn nữa — cuộn hay đổi kích thước cửa sổ thì
// đóng lại, để nó không lơ lửng giữa màn hình cách xa ô chọn mà nó thuộc về.
window.addEventListener('scroll', closeAllSearchableSelect, true);
window.addEventListener('resize', closeAllSearchableSelect);
// Bấm ra ngoài thì đóng hết — đây cũng là lúc dọn những bảng chọn còn sót lại ở <body> sau khi ô chọn của
// chúng bị gỡ khỏi trang (VD người dùng xóa một bước duyệt).
document.addEventListener('click', closeAllSearchableSelect);

// ==================== MENU XỔ XUỐNG DÙNG CHUNG ====================
/**
 * Biến một nút thành nút có menu xổ xuống.
 *
 * @param {string} buttonId    id của nút đã có sẵn trong DOM.
 * @param {Array}  itemList    [{ icon, label, note, onClick }] — mỗi mục một dòng trong menu.
 *
 * Menu được gắn vào chính vùng chứa nút (position: relative) chứ không phải <body>: nút này nằm trong các
 * khối được vẽ lại liên tục (đổi tab, nhập xong tải lại danh sách), gắn ra ngoài body thì mỗi lần vẽ lại
 * sẽ bỏ lại một menu mồ côi không ai dọn.
 */
function attachDropdown(buttonId, itemList) {
    const button = document.getElementById(buttonId);
    if (!button) return;

    const wrapper = document.createElement('span');
    wrapper.className = 'dropdown-wrap';
    button.parentNode.insertBefore(wrapper, button);
    wrapper.appendChild(button);

    const menu = document.createElement('div');
    menu.className = 'dropdown-menu';
    menu.innerHTML = itemList.map((item, i) => `
        <button type="button" class="dropdown-item" data-index="${i}">
            <i class="ph ${esc(item.icon)}"></i>
            <span class="dropdown-item-text">
                <b>${esc(item.label)}</b>
                ${item.note ? `<small>${esc(item.note)}</small>` : ''}
            </span>
        </button>`).join('');
    wrapper.appendChild(menu);

    /**
     * Đặt menu ngay dưới nút, đo theo khung nhìn.
     *
     * Phải tính bằng tay vì menu dùng position: fixed để thoát khỏi overflow: hidden của thẻ chứa nút (xem
     * ghi chú ở .dropdown-menu trong style.css) — mà fixed thì không còn tự bám theo nút được nữa.
     */
    const place = () => {
        // Nút đã bị gỡ khỏi trang (đổi tab, tải lại danh sách) thì thôi bám theo, khỏi tính trên số rác.
        if (!button.isConnected) { stopTracking(); return; }

        const rect = button.getBoundingClientRect();
        const margin = 8;

        // Canh mép phải menu thẳng mép phải nút, nhưng không để lọt ra ngoài khung nhìn.
        const left = Math.min(
            Math.max(margin, rect.right - menu.offsetWidth),
            window.innerWidth - menu.offsetWidth - margin);

        // Không đủ chỗ bên dưới thì lật lên trên — menu bốn mục cao hơn khoảng trống còn lại ở cuối trang.
        // Trừ khi phía trên còn chật hơn, khi đó cứ để xuống dưới cho đỡ bị cắt hơn.
        const roomBelow = window.innerHeight - rect.bottom - margin;
        const top = roomBelow >= menu.offsetHeight || rect.top < menu.offsetHeight + margin
            ? rect.bottom + 6
            : rect.top - menu.offsetHeight - 6;

        menu.style.left = `${left}px`;
        menu.style.top = `${top}px`;
    };

    // Menu không còn bám theo nút nữa nên phải tự đi theo khi trang cuộn hoặc cửa sổ đổi kích thước. Nghe ở
    // pha bắt để bắt được cả những vùng cuộn riêng bên trong trang chứ không chỉ cuộn cả cửa sổ.
    const startTracking = () => {
        window.addEventListener('scroll', place, true);
        window.addEventListener('resize', place);
    };
    const stopTracking = () => {
        window.removeEventListener('scroll', place, true);
        window.removeEventListener('resize', place);
    };

    const close = () => {
        menu.classList.remove('open');
        button.setAttribute('aria-expanded', 'false');
        stopTracking();
    };

    button.setAttribute('aria-haspopup', 'true');
    button.setAttribute('aria-expanded', 'false');
    button.addEventListener('click', (e) => {
        e.stopPropagation();
        // Chỉ một menu được mở tại một thời điểm — hai menu chồng nhau thì không biết đang chọn của cái nào.
        document.querySelectorAll('.dropdown-menu.open').forEach(m => { if (m !== menu) m.classList.remove('open'); });

        const open = menu.classList.toggle('open');
        button.setAttribute('aria-expanded', String(open));

        // Đo sau khi đã mở: lúc còn display none thì menu chưa có kích thước để canh theo.
        if (open) { place(); startTracking(); } else stopTracking();
    });

    menu.querySelectorAll('.dropdown-item').forEach(el => el.addEventListener('click', (e) => {
        e.stopPropagation();
        close();
        itemList[+el.dataset.index].onClick();
    }));

    document.addEventListener('click', close);
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
}

/** Đổ nội dung vào vùng hành động của topbar. */
function setTopbarAction(html) {
    const box = document.getElementById('topbar-action');
    if (box) box.innerHTML = html;
}

// ==================== KHUNG TRANG CỦA SCO ====================
//
// Bài học / bài kiểm tra / mô phỏng là SCO của khóa học SCORM, chạy TRONG khung nội dung của trình phát
// (pages/scorm-player.html). Trình phát đã có mục lục, nút Tiếp/Trước và tên khóa học — dựng thêm thanh bên và
// thanh trên ở đây chỉ phí chỗ của nội dung. Nên khung SCO chỉ có một dòng tiêu đề mỏng và vùng nội dung.

/** Dựng khung trang SCO. Gọi sau Lang.ready + Auth.require(), giống mountLayout. */
function mountScoLayout(title, subHtml = '') {
    ensurePhosphorIcons();
    ensureUicons();
    document.body.classList.add('sco-mode');
    document.body.insertAdjacentHTML('afterbegin', `
        <header class="sco-head">
            <div class="sco-head-text">
                <h1 id="sco-title">${esc(title)}</h1>
                <div class="sco-sub" id="sco-sub">${subHtml}</div>
            </div>
            <div class="sco-head-actions" id="sco-actions"></div>
        </header>`);
}

/** Đổi tiêu đề / dòng phụ / nút của khung SCO. Tham số undefined = giữ nguyên phần đó. */
function setScoHead(title, subHtml, actionHtml) {
    if (title !== undefined) document.getElementById('sco-title').textContent = title;
    if (subHtml !== undefined) document.getElementById('sco-sub').innerHTML = subHtml;
    if (actionHtml !== undefined) document.getElementById('sco-actions').innerHTML = actionHtml;
}

// ==================== MENU XỔ CHO NÚT "…" ====================
//
// Thanh trên chỉ đủ chỗ cho một nút chính và một nút phụ. Những việc còn lại của trang (nhập tệp, xuất
// tệp, tải mẫu...) gom hết vào đây: vẫn bấm tới được, nhưng không chiếm chỗ và không tranh sự chú ý với
// nút chính.
//
// Menu gắn thẳng vào <body> rồi định vị bằng tọa độ màn hình, chứ không đặt lồng trong nút: thanh trên
// là sticky nên có ngữ cảnh xếp lớp riêng — thả menu vào trong đó là nó chui xuống dưới nội dung trang.

let popMenuEl = null;

function closePopMenu() {
    popMenuEl?.remove();
    popMenuEl = null;
}

/** Đóng mọi bảng đang xổ ra từ thanh trên, TRỪ cái vừa được bấm.
 *
 *  Bốn bảng — chuông thông báo, hộp chat, danh sách ngôn ngữ, menu ⋯ — bung ra từ mấy cái nút đứng sát
 *  nhau, nên hai cái cùng mở là chúng đè lên nhau và cái bị che vẫn bắt được cú bấm chuột.
 *
 *  Trước đây mỗi bảng tự đóng vài bảng khác mà nó TÌNH CỜ biết tên, nên lưới đó thủng nhiều chỗ. Tệ hơn:
 *  ba trong bốn nút gọi e.stopPropagation() để cú bấm mở bảng không lọt xuống document rồi đóng ngay chính
 *  bảng vừa mở — mà cú chặn đó giết luôn mấy listener trên document đang lo việc đóng hộ các bảng KHÁC.
 *  Kết quả: bấm chuông trong lúc hộp chat đang mở thì hai bảng cùng nằm đó.
 *
 *  Gom về MỘT chỗ thì thêm bảng mới chỉ phải sửa đúng hàm này, không phải đi lục lại từng bảng cũ.
 */
function closeTopbarOverlay(except = null) {
    if (except !== 'notif') document.getElementById('notif-dropdown')?.classList.add('hidden');

    // Gọi ChatBox.close() chứ không chỉ giấu #chat-panel đi: hộp chat còn phải hủy lượt ghi âm đang dở,
    // không thì đèn micro trên máy vẫn sáng sau khi bảng đã biến mất khỏi màn hình.
    // ChatBox là const cấp cao nhất của một classic script nên KHÔNG nằm trên window — phải hỏi bằng tên
    // trần qua typeof (xem ghi chú ở mountChatWidget). Trang không nạp chat.js thì nó đơn giản là chưa khai.
    if (except !== 'chat' && typeof ChatBox !== 'undefined') ChatBox.close();

    if (except !== 'lang' && typeof closeLangMenu === 'function') closeLangMenu();
    if (except !== 'pop') closePopMenu();
}

/**
 * Mở menu neo dưới một nút.
 * @param {HTMLElement} anchorEl nút bấm để neo tọa độ
 * @param {Array<{icon?: string, label: string, onClick?: Function, danger?: boolean}>} itemList
 */
function openPopMenu(anchorEl, itemList) {
    closePopMenu();
    closeTopbarOverlay('pop');   // hai bảng cùng bung ra một lúc thì không bảng nào là bảng đang được chọn

    const menu = document.createElement('div');
    menu.className = 'pop-menu';
    menu.setAttribute('role', 'menu');

    itemList.forEach(item => {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.setAttribute('role', 'menuitem');
        if (item.danger) btn.className = 'danger';
        btn.innerHTML = `${item.icon ? `<i class="${esc(item.icon)}"></i>` : ''}<span>${esc(item.label)}</span>`;
        btn.addEventListener('click', () => { closePopMenu(); item.onClick?.(); });
        menu.appendChild(btn);
    });

    document.body.appendChild(menu);
    popMenuEl = menu;

    const rect = anchorEl.getBoundingClientRect();
    const width = menu.offsetWidth;
    menu.style.left = `${Math.min(Math.max(8, rect.right - width), window.innerWidth - width - 8)}px`;
    menu.style.top = `${rect.bottom + 6}px`;
    return menu;
}

/** Gắn hành vi bật/tắt menu vào một nút. itemListFn chạy lại mỗi lần mở, để menu bám trạng thái mới nhất. */
function bindPopMenu(btn, itemListFn) {
    if (!btn) return;
    btn.addEventListener('click', (e) => {
        e.stopPropagation();   // không chặn thì chính cú bấm này lọt xuống document và đóng ngay menu vừa mở
        if (popMenuEl) closePopMenu(); else openPopMenu(btn, itemListFn());
    });
}

// Đóng khi bấm ra ngoài, bấm Esc, hoặc khi trang cuộn / đổi kích thước — lúc đó tọa độ đã tính không còn
// đúng nữa, để nguyên thì menu trôi lơ lửng giữa màn hình.
document.addEventListener('click', () => closePopMenu());
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closePopMenu(); });
window.addEventListener('resize', () => closePopMenu());
window.addEventListener('scroll', () => closePopMenu(), true);

const emptyHtml = (text = t('common.empty', 'Chưa có dữ liệu.'), icon = '<i class="ph ph-tray"></i>') =>
    `<div class="empty"><div class="big">${icon}</div>${esc(text)}</div>`;

// ==================== KHUNG XƯƠNG (SKELETON) ====================
// Dùng thay loadingHtml() ở mọi chỗ ĐANG TẢI DỮ LIỆU ĐỌC. Vòng xoay chỉ báo "đang chờ" và làm vùng nội
// dung co lại còn vài chục pixel; khung xương giữ đúng chỗ, đúng hình hài của thứ sắp hiện ra, nên lúc dữ
// liệu về không có cú giật bố cục nào.
//
// Cách dùng: skeletonHtml('<dạng>', { ...tùy chọn }) — xem từng dạng ở SKELETON_SHAPES bên dưới.

/** Chuỗi độ rộng lặp lại cho các dòng chữ giả. Dòng chữ thật không bao giờ dài bằng nhau, nên khung xương
 *  cũng phải so le — vẽ đều tăm tắp nhìn ra ngay là hình giả. */
const SKEL_WIDTHS = ['skel-w-80', 'skel-w-60', 'skel-w-90', 'skel-w-50', 'skel-w-70', 'skel-w-40'];
const skelWidth = (i) => SKEL_WIDTHS[i % SKEL_WIDTHS.length];

/** Lặp `n` lần một hàm dựng chuỗi rồi nối lại — viết tắt cho `Array.from(...).join('')` lặp đi lặp lại. */
const skelRepeat = (n, fn) => Array.from({ length: n }, (_, i) => fn(i)).join('');

const SKELETON_SHAPES = {
    /** Dòng bảng. Trả về thẳng các thẻ <tr>, gán vào tbody.innerHTML.
     *  { rows: số dòng, cols: số cột } */
    table: ({ rows = 6, cols = 5 } = {}) => skelRepeat(rows, () =>
        `<tr class="skel-row" aria-hidden="true">${skelRepeat(cols, (c) =>
            `<td><div class="skel skel-line ${skelWidth(c)}"></div></td>`)}</tr>`),

    /** Lưới thẻ có ảnh bìa — khóa học, sản phẩm cửa hàng.
     *  { count: số thẻ, thumb: có ảnh bìa hay không } */
    cards: ({ count = 6, thumb = true } = {}) => `<div class="skel-grid">${skelRepeat(count, () =>
        `<div class="skel-card">
            ${thumb ? '<div class="skel skel-thumb"></div>' : ''}
            <div class="skel-card-body">
                <div class="skel skel-title skel-w-80"></div>
                <div class="skel skel-line sm skel-w-50"></div>
                <div class="skel skel-btn skel-w-full"></div>
            </div>
        </div>`)}</div>`,

    /** Danh sách dọc: ảnh đại diện tròn + hai dòng chữ. Bảng xếp hạng, danh sách người dùng, giỏ hàng.
     *  { count: số mục, avatar: có vòng tròn bên trái hay không } */
    list: ({ count = 5, avatar = true } = {}) => `<div class="skel-wrap">${skelRepeat(count, (i) =>
        `<div class="skel-item">
            ${avatar ? '<div class="skel skel-circle" style="width:38px;height:38px;"></div>' : ''}
            <div class="skel-item-text">
                <div class="skel skel-line ${skelWidth(i)}"></div>
                <div class="skel skel-line sm skel-w-40"></div>
            </div>
        </div>`)}</div>`,

    /** Ô số liệu tổng quan đầu trang thống kê. { count: số ô } */
    stats: ({ count = 4 } = {}) => `<div class="skel-grid skel-grid-stat">${skelRepeat(count, () =>
        `<div class="skel-stat">
            <div class="skel skel-line sm skel-w-50"></div>
            <div class="skel skel-title skel-w-40"></div>
        </div>`)}</div>`,

    /** Khối chi tiết: một tiêu đề rồi mấy dòng nội dung. { lines: số dòng dưới tiêu đề } */
    detail: ({ lines = 4 } = {}) => `<div class="skel-wrap skel-stack">
        <div class="skel skel-title skel-w-50"></div>
        ${skelRepeat(lines, (i) => `<div class="skel skel-line ${skelWidth(i)}"></div>`)}
    </div>`,

    /** Chỉ mấy dòng chữ, không tiêu đề. { lines: số dòng } */
    text: ({ lines = 3 } = {}) => `<div class="skel-wrap skel-stack">${skelRepeat(lines, (i) =>
        `<div class="skel skel-line ${skelWidth(i)}"></div>`)}</div>`,

    /** Một mảng lớn đặc giữ chỗ — biểu đồ, khung xem trước, trình phát video.
     *  { height: chiều cao CSS } */
    block: ({ height = '220px' } = {}) =>
        `<div class="skel-wrap"><div class="skel skel-block" style="height:${height};"></div></div>`
};

/**
 * Dựng khung xương cho vùng đang tải.
 * @param {'table'|'cards'|'list'|'stats'|'detail'|'text'|'block'} shape dạng khung, mặc định 'text'
 * @param {object} options tùy chọn riêng của từng dạng (xem SKELETON_SHAPES)
 */
function skeletonHtml(shape = 'text', options = {}) {
    const build = SKELETON_SHAPES[shape] || SKELETON_SHAPES.text;
    // aria-busy + aria-hidden để trình đọc màn hình bỏ qua đống ô rỗng này thay vì đọc ra một mớ vô nghĩa;
    // dạng 'table' không bọc được vào <div> nên tự gắn aria-hidden lên từng <tr>.
    if (shape === 'table') return build(options);
    return `<div role="status" aria-busy="true" aria-label="${esc(t('common.loading', 'Đang tải dữ liệu...'))}">${build(options)}</div>`;
}
