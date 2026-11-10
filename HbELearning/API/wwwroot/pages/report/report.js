// File: wwwroot/pages/report/report.js
(async function () {
    await Lang.ready;

    if (!Auth.require(['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'], 'report.view')) return;

    // Khai báo hết hằng và trạng thái TRƯỚC khi gọi bất kỳ hàm nào bên dưới: phần khởi tạo nằm cuối hàm và các
    // nút thẻ gọi loadTab() ngay khi bấm — một "const" nằm phía sau lời gọi đầu tiên sẽ còn trong Temporal Dead Zone.
    const isHeadOfDepartment = Auth.hasRole('Trưởng phòng');
    const isDirector = Auth.hasRole('Giám Đốc');
    const defaultTab = isHeadOfDepartment ? 'learner' : isDirector ? 'department' : 'course';

    const $ = (id) => document.getElementById(id);
    const fmt = (n) => Number(n || 0).toLocaleString(Lang.locale);
    const fmtPct = (n) => `${Number(n || 0).toLocaleString(Lang.locale, { maximumFractionDigits: 1 })}%`;
    const fmtScore = (n) => n == null ? '—' : Number(n).toLocaleString(Lang.locale, { maximumFractionDigits: 1 });

    // Trạng thái mỗi bảng: dữ liệu gốc + cột đang sắp xếp + chuỗi đang tìm. Bảng được dựng khung một lần,
    // sau đó chỉ vẽ lại phần thân khi sắp xếp / tìm kiếm — dựng lại cả khối sẽ làm ô tìm kiếm mất con trỏ giữa lúc gõ.
    const tableState = {};
    let drawerOpener = null;

    mountLayout('report', t('report.baoCaoDaoTao', 'Báo cáo đào tạo'), isHeadOfDepartment || isDirector
        ? t('report.thongKeKetQuaDaoTaoCuaPhongBan', 'Thống kê kết quả đào tạo của phòng ban bạn quản lý')
        : t('report.thongKeKetQuaDaoTaoToanDoanhNg', 'Thống kê tổng quan toàn doanh nghiệp'));

    setTopbarAction('<button class="btn ghost" onclick="window.print()"><i class="ph ph-printer"></i> <span data-i18n="report.inBaoCao">In báo cáo</span></button>');

    if (isHeadOfDepartment) {
        $('overview').style.display = 'none';
        document.querySelector('.tabs-container').style.display = 'none';
        $('tab-course').classList.remove('active');
        $('tab-learner').classList.add('active');
    } else if (isDirector) {
        $('overview').style.display = 'none';
        document.querySelector('[data-tab="course"]').style.display = 'none';
        document.querySelector('[data-tab="course"]').classList.remove('active');
        $('tab-course').classList.remove('active');
        document.querySelector('[data-tab="department"]').classList.add('active');
        $('tab-department').classList.add('active');
    }

    document.querySelectorAll('.tabs button').forEach(button => {
        button.addEventListener('click', () => {
            showTab(button.dataset.tab, defaultTab);
            loadTab(button.dataset.tab);
        });
    });

    // ==========================================
    // 1. DẢI CHỈ SỐ + PHÂN BỐ TRẠNG THÁI
    // ==========================================
    async function loadOverview() {
        const box = $('overview');
        box.innerHTML = skeletonHtml('stats', { count: 4 }) + skeletonHtml('block', { height: '150px' });
        try {
            const o = await API.get('/report/overview');
            box.innerHTML = `<div class="ops">${kpiHtml(o)}${statusPanelHtml(o)}</div>`;
            bindStatusTip();
        } catch (ex) {
            box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        }
    }

    /** Bốn chỉ số, cùng dải liền chia ô của trang Tổng quan.
     *   • Lượt đào tạo     = số bản ghi phân công khóa học chưa bị hủy (đếm LƯỢT, không đếm người).
     *   • Tỷ lệ hoàn thành = lượt Hoàn thành / lượt đào tạo.
     *   • Tiến độ trung bình = trung bình ProgressPercent của các lượt chưa bị hủy.
     *   • Quá hạn          = lượt có hạn hoàn thành đã qua mà chưa Hoàn thành (xem ReportService.GetOverview). */
    function kpiHtml(o) {
        const total = o.totalEnrollment || 0;
        const unfinished = Math.max(0, total - (o.completedEnrollment || 0));
        const overdue = o.overdueEnrollment || 0;
        const overduePercent = unfinished ? Math.round(overdue * 100 / unfinished) : 0;

        const cellList = [
            {
                tone: 'var(--primary)', icon: 'ph-clipboard-text',
                label: t('report.tongLuotDaoTao', 'Tổng lượt đào tạo'), value: fmt(total),
                foot: t('report.kpiEnrollmentFoot', '{count} khóa học đang hiện hành', { count: fmt(o.publishedCourse) }),
            },
            {
                tone: 'var(--viz-done)', icon: 'ph-seal-check',
                label: t('dashboard.completionRate', 'Tỷ lệ hoàn thành'), value: fmtPct(o.completionRate),
                foot: t('report.kpiCompletionFoot', '{done}/{total} lượt đã hoàn thành', { done: fmt(o.completedEnrollment), total: fmt(total) }),
            },
            {
                tone: 'var(--info)', icon: 'ph-chart-line-up',
                label: t('dashboard.averageProgress', 'Tiến độ trung bình'), value: fmtPct(o.averageProgress),
                foot: t('report.kpiProgressFoot', '{count} lượt đang học', { count: fmt(o.inProgressEnrollment) }),
            },
            {
                tone: overdue ? 'var(--danger)' : 'var(--text-muted)', icon: 'ph-alarm',
                label: t('dashboard.overdue', 'Quá hạn'), value: fmt(overdue),
                foot: overdue
                    ? t('report.kpiOverdueFoot', '{percent}% số lượt chưa hoàn thành', { percent: overduePercent })
                    : t('report.kpiOverdueNone', 'Không có lượt nào quá hạn'),
            },
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

    /** Thanh phân bố trạng thái. Ba nhóm đầu là thang thứ tự một sắc (đậm = đã đi xa hơn); "Không đạt" dùng sắc cam
     *  của "chưa hoàn thành" ở trang Tổng quan và chỉ hiện khi thật sự có. Số "không đạt" không có sẵn trong API nên
     *  suy ra từ phần còn lại: lượt đào tạo - hoàn thành - đang học - chưa bắt đầu. */
    function statusPanelHtml(o) {
        const total = o.totalEnrollment || 0;
        const done = o.completedEnrollment || 0;
        const progress = o.inProgressEnrollment || 0;
        const idle = o.notStartedEnrollment || 0;
        const failed = Math.max(0, total - done - progress - idle);
        const percentOf = (n) => total ? n * 100 / total : 0;

        const segList = [
            { label: t('dashboard.completed', 'Đã hoàn thành'), n: done, color: 'var(--viz-done)' },
            { label: t('dashboard.inProgress', 'Đang học'), n: progress, color: 'var(--viz-mid)' },
            { label: t('dashboard.notStarted', 'Chưa bắt đầu'), n: idle, color: 'var(--viz-low)' },
            { label: t('shell.khongDat', 'Không đạt'), n: failed, color: 'var(--viz-open)', optional: true },
        ].filter(s => !s.optional || s.n > 0);

        const barHtml = total
            ? `<div class="rpt-bar" role="group" aria-label="${esc(t('report.statusAria', 'Phân bố trạng thái học tập'))}">
                ${segList.filter(s => s.n > 0).map(s => `
                    <span class="rpt-seg" tabindex="0" role="img" style="--v:${s.n};--c:${s.color}"
                          aria-label="${esc(`${s.label}: ${fmt(s.n)} (${fmtPct(percentOf(s.n))})`)}"
                          data-label="${esc(s.label)}" data-n="${s.n}" data-pct="${percentOf(s.n)}" data-color="${s.color}"></span>`).join('')}
               </div>`
            : `<div class="rpt-bar is-empty"></div>
               <div class="rpt-empty">${esc(t('report.statusEmpty', 'Chưa có lượt đào tạo nào được giao.'))}</div>`;

        const legendHtml = `<ul class="rpt-legend">${segList.map(s => `
            <li><i style="background:${s.color}"></i><span>${esc(s.label)}</span>
                <b>${fmt(s.n)}</b><small>${total ? fmtPct(percentOf(s.n)) : '—'}</small></li>`).join('')}</ul>`;

        const noteHtml = !total ? ''
            : o.overdueEnrollment > 0
                ? `<div class="rpt-note bad"><i class="ph-fill ph-warning-circle"></i>
                       <span>${esc(t('report.overdueNote', '{count} lượt đã quá hạn — nằm trong các nhóm chưa hoàn thành ở trên', { count: fmt(o.overdueEnrollment) }))}</span></div>`
                : `<div class="rpt-note ok"><i class="ph-fill ph-check-circle"></i>
                       <span>${esc(t('report.kpiOverdueNone', 'Không có lượt nào quá hạn'))}</span></div>`;

        return `<section class="ops-trend" id="rpt-status">
            <div class="ops-trend-head">
                <div>
                    <h3>${esc(t('report.tyTrongTrangThaiHocTap', 'Tỷ trọng trạng thái học tập'))}</h3>
                    <p>${esc(t('report.statusSub', '{count} lượt đào tạo, không tính lượt đã hủy', { count: fmt(total) }))}</p>
                </div>
            </div>
            <div class="rpt-status-body">${barHtml}${total ? legendHtml : ''}${noteHtml}</div>
            <div class="trend-tip" id="status-tip" role="tooltip"></div>
        </section>`;
    }

    /** Chú thích nổi dùng chung cho mọi khúc. Số liệu đọc từ data-* và gắn bằng textContent — tên nhóm không bao giờ
     *  đi qua innerHTML. Mọi con số trong chú thích cũng nằm sẵn ở chú thích dưới thanh nên không có gì chỉ đọc được bằng chuột. */
    function bindStatusTip() {
        const panel = $('rpt-status');
        const bar = panel?.querySelector('.rpt-bar');
        const tip = $('status-tip');
        if (!bar || !tip) return;

        const hide = () => tip.classList.remove('on');
        const show = (seg) => {
            if (!seg) { hide(); return; }
            const line = (className, text) => {
                const node = document.createElement('div');
                node.className = className;
                node.textContent = text;
                return node;
            };
            const row = document.createElement('div');
            row.className = 'tt-row';
            const key = document.createElement('i');
            key.className = 'tt-key';
            key.style.background = seg.dataset.color;
            const number = document.createElement('b');
            number.textContent = fmt(seg.dataset.n);
            row.append(key, number, t('report.tipUnit', 'lượt'));

            tip.replaceChildren(
                line('tt-title', seg.dataset.label),
                row,
                line('tt-foot', t('report.tipShare', '{percent}% tổng số lượt', {
                    percent: Number(seg.dataset.pct).toLocaleString(Lang.locale, { maximumFractionDigits: 1 }),
                })));

            const panelRect = panel.getBoundingClientRect();
            const segRect = seg.getBoundingClientRect();
            const half = tip.offsetWidth / 2;
            const centerX = segRect.left + segRect.width / 2 - panelRect.left;
            tip.style.left = `${Math.min(Math.max(centerX, half + 8), panelRect.width - half - 8)}px`;
            tip.style.top = `${segRect.top - panelRect.top}px`;
            tip.classList.add('on');
        };

        bar.addEventListener('pointerover', e => show(e.target.closest('.rpt-seg')));
        bar.addEventListener('pointerleave', hide);
        bar.addEventListener('focusin', e => show(e.target.closest('.rpt-seg')));
        bar.addEventListener('focusout', hide);
    }

    // ==========================================
    // 2. BẢNG BÁO CÁO (sắp xếp + tìm kiếm)
    // ==========================================
    const nameCell = (name, code) => `<div class="rpt-name"><b>${esc(name)}</b><small>${esc(code)}</small></div>`;
    const overdueCell = (n) => n > 0
        ? `<span class="rpt-bad"><i class="ph-fill ph-warning"></i>${fmt(n)}</span>`
        : '<span class="muted">0</span>';

    // Mỗi cột: key, label, num (căn phải), wide, sort (hàm lấy giá trị để sắp; thiếu = cột không sắp được), cell (HTML ô).
    const tableDefs = {
        course: () => ({
            title: t('report.hieuQuaTheoKhoaHoc', 'Hiệu quả theo khóa học'),
            sortKey: 'assignedCount', sortDir: -1,
            search: r => `${r.courseTitle} ${r.courseCode}`,
            columnList: [
                { key: 'courseTitle', label: t('report.colCourse', 'Khóa học'), sort: r => r.courseTitle, cell: r => nameCell(r.courseTitle, r.courseCode) },
                { key: 'status', label: t('report.colStatus', 'Trạng thái'), cell: r => statusBadge(r.status) },
                { key: 'assignedCount', label: t('report.colAssigned', 'Phân công'), num: true, sort: r => r.assignedCount, cell: r => fmt(r.assignedCount) },
                { key: 'completedCount', label: t('report.colDone', 'Hoàn thành'), num: true, sort: r => r.completedCount, cell: r => fmt(r.completedCount) },
                { key: 'overdueCount', label: t('report.colOverdue', 'Quá hạn'), num: true, sort: r => r.overdueCount, cell: r => overdueCell(r.overdueCount) },
                { key: 'completionRate', label: t('report.colRate', 'Tỷ lệ hoàn thành'), wide: true, sort: r => r.completionRate, cell: r => progressBar(r.completionRate) },
                { key: 'averageScore', label: t('report.colScore', 'Điểm TB'), num: true, sort: r => r.averageScore, cell: r => fmtScore(r.averageScore) },
                { key: 'certificateCount', label: t('report.colCertificate', 'Chứng nhận'), num: true, sort: r => r.certificateCount, cell: r => fmt(r.certificateCount) },
                { key: 'averageRating', label: t('report.danhGia', 'Đánh giá'), num: true, sort: r => r.averageRating,
                  cell: r => r.averageRating != null
                      ? `<span class="rpt-star"><i class="ph-fill ph-star"></i></span> ${Number(r.averageRating).toFixed(1)} <small class="muted">(${fmt(r.reviewCount)})</small>`
                      : '—' },
            ],
        }),
        department: () => ({
            title: t('report.ketQuaDaoTaoTheoPhongBan', 'Kết quả đào tạo theo phòng ban'),
            sortKey: null, sortDir: 1,
            search: r => `${r.departmentName} ${r.departmentCode}`,
            columnList: [
                { key: 'departmentName', label: t('report.colDepartment', 'Phòng ban'), sort: r => r.departmentName, cell: r => nameCell(r.departmentName, r.departmentCode) },
                { key: 'userCount', label: t('report.colStaff', 'Nhân sự'), num: true, sort: r => r.userCount, cell: r => fmt(r.userCount) },
                { key: 'assignedCount', label: t('report.colAssignment', 'Lượt phân công'), num: true, sort: r => r.assignedCount, cell: r => fmt(r.assignedCount) },
                { key: 'completedCount', label: t('report.colDone', 'Hoàn thành'), num: true, sort: r => r.completedCount, cell: r => fmt(r.completedCount) },
                { key: 'completionRate', label: t('report.colRate', 'Tỷ lệ hoàn thành'), wide: true, sort: r => r.completionRate, cell: r => progressBar(r.completionRate) },
                { key: 'averageScore', label: t('report.colScore', 'Điểm TB'), num: true, sort: r => r.averageScore, cell: r => fmtScore(r.averageScore) },
            ],
        }),
        learner: () => ({
            title: t('report.ketQuaDaoTaoTheoNguoiHoc', 'Kết quả đào tạo theo người học'),
            sortKey: null, sortDir: 1,
            search: r => `${r.fullName} ${r.employeeCode} ${r.departmentName || ''}`,
            columnList: [
                { key: 'fullName', label: t('report.colName', 'Họ và tên'), sort: r => r.fullName,
                  cell: r => `<div class="rpt-person"><span class="avatar">${esc(initial(r.fullName))}</span>${nameCell(r.fullName, r.employeeCode)}</div>` },{ key: 'fullName', label: t('report.colName', 'Họ và tên'), sort: r => r.fullName,
                  cell: r => `<div class="rpt-person"><span class="avatar">${esc(initial(r.fullName))}</span>${nameCell(r.fullName, r.employeeCode)}</div>` },
                { key: 'departmentName', label: t('report.colDepartment', 'Phòng ban'), sort: r => r.departmentName, cell: r => `<span class="muted">${esc(r.departmentName || '—')}</span>` },
                { key: 'assignedCount', label: t('report.colAssigned', 'Phân công'), num: true, sort: r => r.assignedCount, cell: r => fmt(r.assignedCount) },
                { key: 'completedCount', label: t('report.colDone', 'Hoàn thành'), num: true, sort: r => r.completedCount, cell: r => fmt(r.completedCount) },
                { key: 'overdueCount', label: t('report.colOverdue', 'Quá hạn'), num: true, sort: r => r.overdueCount, cell: r => overdueCell(r.overdueCount) },
                { key: 'averageProgress', label: t('report.colProgress', 'Tiến độ TB'), wide: true, sort: r => r.averageProgress, cell: r => progressBar(r.averageProgress) },
                { key: 'averageScore', label: t('report.colScore', 'Điểm TB'), num: true, sort: r => r.averageScore, cell: r => fmtScore(r.averageScore) },
                { key: 'action', label: '', cls: 'no-print text-right',
                  cell: r => `<button type="button" class="btn ghost sm" data-learner-detail="${r.userId}">${esc(t('report.detail', 'Chi tiết'))}</button>` },
            ],
        }),
    };

    /** Giá trị rỗng luôn xuống cuối dù sắp tăng hay giảm — "chưa có điểm" không phải điểm thấp nhất. */
    const compareValue = (a, b, dir) => {
        const aNone = a == null, bNone = b == null;
        if (aNone || bNone) return aNone === bNone ? 0 : aNone ? 1 : -1;
        return (typeof a === 'string' ? a.localeCompare(b, Lang.locale) : a - b) * dir;
    };

    async function loadTab(tab) {
        const box = $(`tab-${tab}`);
        box.innerHTML = skeletonHtml('list', { count: 6, avatar: false });
        try {
            mountTable(tab, await API.get(`/report/${tab}`));
        } catch (ex) { box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>'); }
    }

    function mountTable(tab, rowList) {
        const box = $(`tab-${tab}`);
        const def = tableDefs[tab]();
        const state = tableState[tab] = { def, rowList, sortKey: def.sortKey, sortDir: def.sortDir, query: '' };

        if (!rowList.length) {
            box.innerHTML = `<div class="card rpt-card">
                <div class="card-head"><h3>${esc(def.title)}</h3></div>
                ${emptyHtml(t('common.empty', 'Chưa có dữ liệu.'), '<i class="ph ph-list" style="font-size:24px; color:var(--text-muted)"></i>')}
            </div>`;
            return;
        }

        const headHtml = def.columnList.map(c => {
            const cls = [c.num ? 'num' : '', c.wide ? 'rpt-wide' : '', c.cls || ''].filter(Boolean).join(' ');
            if (!c.sort) return `<th class="${cls}">${esc(c.label)}</th>`;
            return `<th class="${cls}" data-col="${c.key}" aria-sort="none">
                <button type="button" class="rpt-sort" data-sort="${c.key}" title="${esc(t('report.sortBy', 'Sắp xếp theo {column}', { column: c.label }))}">
                    ${esc(c.label)}<i class="ph ph-caret-up-down"></i></button></th>`;
        }).join('');

        // Bảng vài dòng thì ô tìm kiếm chỉ thêm rối.
        const searchHtml = rowList.length > 6
            ? `<label class="rpt-search no-print"><i class="ph ph-magnifying-glass"></i>
                   <input type="search" data-search placeholder="${esc(t('report.searchPlaceholder', 'Tìm trong bảng...'))}"
                          aria-label="${esc(t('report.searchPlaceholder', 'Tìm trong bảng...'))}"></label>`
            : '';

        box.innerHTML = `<div class="card rpt-card">
            <div class="card-head">
                <div class="rpt-title"><h3>${esc(def.title)}</h3><span class="badge gray">${fmt(rowList.length)}</span></div>
                ${searchHtml}
            </div>
            <div class="table-wrap"><table class="rpt-table">
                <thead><tr>${headHtml}</tr></thead>
                <tbody></tbody>
            </table></div>
        </div>`;

        box.querySelector('[data-search]')?.addEventListener('input', e => {
            state.query = e.target.value;
            paintTable(tab);
        });
        box.querySelector('thead').addEventListener('click', e => {
            const button = e.target.closest('[data-sort]');
            if (!button) return;
            // Bấm lại đúng cột đang sắp thì đảo chiều; sang cột khác thì bắt đầu từ chiều hợp lý của kiểu dữ liệu
            // (chữ: A→Z, số: lớn trước — người đọc báo cáo thường tìm cái lớn nhất).
            if (state.sortKey === button.dataset.sort) state.sortDir *= -1;
            else {
                state.sortKey = button.dataset.sort;
                state.sortDir = def.columnList.find(c => c.key === state.sortKey).num ? -1 : 1;
            }
            paintTable(tab);
        });
        box.querySelector('tbody').addEventListener('click', e => {
            const button = e.target.closest('[data-learner-detail]');
            if (button) showLearnerDetail(+button.dataset.learnerDetail, button);
        });

        paintTable(tab);
    }

    function paintTable(tab) {
        const state = tableState[tab];
        const box = $(`tab-${tab}`);
        const { def } = state;
        const query = state.query.trim().toLowerCase();

        const shown = query ? state.rowList.filter(r => def.search(r).toLowerCase().includes(query)) : state.rowList.slice();
        const sortColumn = def.columnList.find(c => c.key === state.sortKey);
        if (sortColumn?.sort) shown.sort((a, b) => compareValue(sortColumn.sort(a), sortColumn.sort(b), state.sortDir));

        box.querySelector('tbody').innerHTML = shown.length
            ? shown.map(r => `<tr>${def.columnList.map(c => {
                const cls = [c.num ? 'num' : '', c.cls || ''].filter(Boolean).join(' ');
                return `<td${cls ? ` class="${cls}"` : ''}>${c.cell(r)}</td>`;
            }).join('')}</tr>`).join('')
            : `<tr><td class="rpt-nomatch" colspan="${def.columnList.length}">${esc(t('report.noMatch', 'Không có dòng nào khớp.'))}</td></tr>`;

        box.querySelectorAll('th[data-col]').forEach(th => {
            const active = th.dataset.col === state.sortKey;
            th.setAttribute('aria-sort', active ? (state.sortDir > 0 ? 'ascending' : 'descending') : 'none');
            th.querySelector('i').className = `ph ${active ? (state.sortDir > 0 ? 'ph-caret-up' : 'ph-caret-down') : 'ph-caret-up-down'}`;
        });
    }

    // ==========================================
    // 3. HỒ SƠ NGƯỜI HỌC (NGĂN KÉO)
    // ==========================================
    const getInitials = (name) => {
        if (!name) return '?';
        return name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();
    };

    function openDrawer(opener) {
        drawerOpener = opener || null;
        $('drawer-overlay').classList.add('open');
        $('drawer-panel').classList.add('open');
        $('drawer-close').focus();
    }

    function closeDrawer() {
        $('drawer-overlay').classList.remove('open');
        $('drawer-panel').classList.remove('open');
        // Trả con trỏ về nút đã mở ngăn kéo — người dùng bàn phím không bị ném lên đầu trang.
        if (drawerOpener && document.contains(drawerOpener)) drawerOpener.focus();
        drawerOpener = null;
    }

    $('drawer-close').setAttribute('aria-label', t('common.close', 'Đóng'));
    $('drawer-overlay').addEventListener('click', closeDrawer);
    $('drawer-close').addEventListener('click', closeDrawer);
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && $('drawer-panel').classList.contains('open')) closeDrawer();
    });

    async function showLearnerDetail(userId, opener) {
        const learner = tableState.learner?.rowList.find(x => x.userId === userId);
        if (!learner) return;

        openDrawer(opener);

        $('drawer-user-info').innerHTML = `
            <div class="drawer-avatar">${esc(getInitials(learner.fullName))}</div>
            <h3 id="drawer-title">${esc(learner.fullName)}</h3>
            <div class="drawer-sub">ID: ${esc(learner.employeeCode)} • ${esc(learner.departmentName || '—')}</div>`;

        const body = $('drawer-body');
        body.innerHTML = skeletonHtml('stats', { count: 2 }) + skeletonHtml('list', { count: 4, avatar: false });

        try {
            const courseList = await API.get(`/enrollment/by-user/${userId}`);

            body.innerHTML = `
                <div class="rpt-d-stats">
                    <div class="rpt-d-stat">
                        <small>${esc(t('report.drawerDone', 'Hoàn thành'))}</small>
                        <b>${fmt(learner.completedCount)} <span>/ ${fmt(learner.assignedCount)}</span></b>
                    </div>
                    <div class="rpt-d-stat">
                        <small>${esc(t('report.drawerProgress', 'Tiến độ trung bình'))}</small>
                        <b>${fmtPct(learner.averageProgress)}</b>
                    </div>
                </div>

                <div class="rpt-d-heading">${esc(t('report.drawerHistory', 'Lịch sử đào tạo'))}</div>

                ${courseList.length ? courseList.map(c => {
                    const isDone = c.progressPercent === 100;
                    return `<div class="drawer-course-card">
                        <div class="rpt-d-row">
                            <span class="rpt-d-title">${esc(c.courseTitle)}</span>
                            ${c.isOverdue ? `<span class="rpt-bad"><i class="ph-fill ph-warning"></i>${esc(t('dashboard.overdue', 'Quá hạn'))}</span>` : ''}
                        </div>
                        <div class="rpt-d-row sub">
                            <span>${esc(t('report.drawerCourseProgress', 'Tiến độ'))}: <strong class="${isDone ? 'is-done' : ''}">${c.progressPercent}%</strong></span>
                            <span>${esc(t('report.drawerScore', 'Điểm'))}: <strong>${c.finalScore ?? '—'}</strong></span>
                        </div>
                        <div class="progress ${isDone ? 'done' : ''}"><span style="width:${c.progressPercent}%"></span></div>
                    </div>`;
                }).join('') : emptyHtml(t('report.chuaDuocPhanCongKhoaHocNao', 'Chưa được phân công khóa học nào.'), '<i class="ph ph-books" style="font-size: 32px; color:var(--text-muted);"></i>')}`;
        } catch (ex) { body.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>'); }
    }

    // ==========================================
    // 4. KHỞI TẠO TRANG
    // ==========================================
    // Trưởng phòng chỉ có đúng một thẻ (thanh thẻ bị ẩn) nên không cần đọc địa chỉ. Còn lại mở lại thẻ ghi trên địa
    // chỉ trang (?tab=): tải lại trang, kể cả khi đổi ngôn ngữ, vẫn đứng nguyên thẻ đang xem.
    if (isHeadOfDepartment) { await loadTab('learner'); }
    else {
        const startTab = tabFromUrl(defaultTab);
        showTab(startTab, defaultTab);
        if (!isDirector) await loadOverview();
        await loadTab(startTab);
    }
})();
