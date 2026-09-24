// File: wwwroot/js/translation.js
// ==================== MÀN HÌNH DỊCH NGÔN NGỮ ====================
//
// Bố cục theo kiểu "bàn làm việc của người dịch": chọn một cặp NGUỒN → ĐÍCH rồi soi từng dòng, thay vì
// bày hết mọi ngôn ngữ ra thành N cột. Đó là cách người ta thật sự dịch — đọc một câu tiếng Việt, viết
// câu tiếng Anh tương ứng. Bày 5 cột cạnh nhau thì mỗi cột hẹp còn 150px, câu nào cũng xuống bốn dòng.
//
// Toàn bộ khóa dịch được tải về MỘT LẦN rồi lọc / phân trang ngay trên máy. Lý do: các bộ lọc chất lượng
// ("thiếu bản dịch", "lệch biến số", "giống hệt nguồn") phải so hai ngôn ngữ với nhau theo từng dòng —
// server hiện chưa có phép lọc đó, mà con số trên mỗi chip thì phải đếm trên TOÀN BỘ chứ không phải
// trên 25 dòng đang xem. Đổi lại, gõ tìm và chuyển trang không tốn thêm một lượt gọi mạng nào.

(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    if (!Auth.require(['Admin'], 'translation.view')) return;
    mountLayout('translation', t('menu.translation', 'Dịch ngôn ngữ'));

    const canCreate = Auth.can('translation.create') ?? true;
    const canUpdate = Auth.can('translation.update') ?? true;
    const canDelete = Auth.can('translation.delete') ?? true;

    // Thanh trên chỉ giữ lại hai việc làm thường xuyên nhất: nhờ AI dịch và thêm một thuật ngữ. Nhập /
    // xuất tệp là việc thỉnh thoảng mới làm — bày cả bốn nút chữ cạnh nhau thì hàng nút dài hơn cả tiêu đề
    // trang, và nút chính chìm nghỉm giữa ba nút viền xám giống hệt nhau.
    setTopbarAction(`
        ${canUpdate ? `<button class="btn ghost" id="btn-ai" title="${esc(t('translation.aiDich', 'Dịch bằng AI'))}"><i class="ph ph-sparkle"></i> <span data-i18n="translation.aiDich">Dịch bằng AI</span></button>` : ''}
        ${canCreate ? `<button class="btn tr-primary" id="btn-add" title="${esc(t('translation.themThuatNgu', 'Thêm thuật ngữ'))}"><i class="ph ph-plus"></i> <span data-i18n="translation.themThuatNgu">Thêm thuật ngữ</span></button>` : ''}
        <button class="topbar-more" type="button" id="btn-more" aria-haspopup="menu"
                title="${esc(t('common.moreActions', 'Việc khác'))}"><i class="ph ph-dots-three-outline"></i></button>`);

    // ==================== TRẠNG THÁI ====================
    const PAGE_SIZE = 25;
    const FETCH_SIZE = 200;          // trần pageSize của API, xem TranslationController.GetList

    const state = {
        languageList: [],
        allRow: [],                  // toàn bộ khóa dịch, đã tải sẵn
        sourceLang: 'vi',
        targetLang: '',
        keyword: '',
        group: '',
        source: '',                  // '', 'system', 'custom', 'customized'
        quality: 'all',              // 'all' | 'missing' | 'placeholder' | 'same' | 'custom'
        page: 1,
        selectedKey: null,           // khóa của dòng đang chọn bằng bàn phím
        editingKey: null,            // khóa của dòng đang mở ô sửa
    };

    let viewRow = [];                // các dòng khớp bộ lọc hiện tại
    let pageRow = [];                // phần đang hiển thị của viewRow

    // ==================== BẢN ĐỒ TRA CỨU ====================
    /** Mã quốc gia hiện trên "lá cờ" chữ. Không có trong bảng thì lấy chính mã ngôn ngữ viết hoa. */
    const COUNTRY_BY_LANG = {
        vi: 'VN', en: 'GB', ja: 'JP', ko: 'KR', zh: 'CN', th: 'TH', km: 'KH', lo: 'LA',
        id: 'ID', ms: 'MY', tl: 'PH', my: 'MM', hi: 'IN', fr: 'FR', de: 'DE', es: 'ES',
        pt: 'BR', it: 'IT', nl: 'NL', ru: 'RU', ar: 'SA',
    };
    const countryOf = (lang) => COUNTRY_BY_LANG[lang] || (lang || '').slice(0, 2).toUpperCase();

    const flagHtml = (lang) => `<span class="tr-flag">${esc(countryOf(lang))}</span>`;

    const labelOf = (lang) => state.languageList.find(x => x.lang === lang)?.label || lang;

    /** Tên màn hình cho người đọc.
     *
     *  Khóa dịch đặt theo tiền tố kỹ thuật ("account.doiMatKhau"), mà người dịch thì nghĩ theo màn hình
     *  họ nhìn thấy. Phần lớn tiền tố trùng khóa menu nên mượn thẳng tên menu đã dịch sẵn; số còn lại là
     *  các màn hình không nằm trên menu (đăng nhập, quên mật khẩu, lời chào...) nên phải khai riêng.
     *  Tiền tố lạ thì để nguyên — thà hiện mã kỹ thuật còn hơn đoán sai tên màn hình. */
    // Mỗi mục gồm khóa dịch VÀ chữ dự phòng: bản dịch thật nằm trong CSDL, mà khóa vừa thêm chỉ vào được
    // CSDL ở lần khởi động sau (xem Data/TranslationSeeder.cs). Không có chữ dự phòng thì từ giờ tới lúc
    // đó cột "Màn hình" hiện toàn mã kỹ thuật.
    const GROUP_LABEL = {
        'course-detail':   ['translation.grpChiTietKhoaHoc', 'Chi tiết khóa học'],
        'my-course':       ['menu.my-course', 'Khóa học của tôi'],
        'online-class':    ['menu.online-class', 'Phòng học online'],
        'classroom-recap': ['translation.grpTongKetBuoiHoc', 'Tổng kết buổi học'],
        classroom:         ['translation.grpLopHoc', 'Lớp học'],
        aidoc:             ['menu.ai-document', 'Tài liệu AI'],
        aitutor:           ['menu.ai-tutor', 'Pico B'],
        account:           ['translation.grpTaiKhoan', 'Tài khoản'],
        index:             ['translation.grpTrangChu', 'Trang chủ'],
        login:             ['translation.grpDangNhap', 'Đăng nhập'],
        forgot:            ['translation.grpQuenMatKhau', 'Quên mật khẩu'],
        fp:                ['translation.grpQuenMatKhau', 'Quên mật khẩu'],
        pwd:               ['translation.grpMatKhau', 'Mật khẩu'],
        shell:             ['translation.grpKhungGiaoDien', 'Khung giao diện'],
        menu:              ['translation.grpMenu', 'Menu'],
        common:            ['translation.grpDungChung', 'Dùng chung'],
        status:            ['translation.grpTrangThai', 'Trạng thái'],
        time:              ['translation.grpThoiGian', 'Thời gian'],
        greeting:          ['translation.grpLoiChao', 'Lời chào'],
        group:             ['translation.grpNhomMenu', 'Nhóm menu'],
        import:            ['translation.grpNhapExcel', 'Nhập Excel'],
        learn:             ['translation.grpHocBai', 'Học bài'],
        quiz:              ['translation.grpBaiKiemTra', 'Bài kiểm tra'],
        simulation:        ['translation.grpMoPhong', 'Mô phỏng'],
        translation:       ['menu.translation', 'Dịch ngôn ngữ'],
    };

    function groupLabel(group) {
        const entry = GROUP_LABEL[group];
        if (entry) return t(entry[0], entry[1]);
        // Còn lại là các tiền tố trùng khóa menu (course, user, report, role...) — mượn tên menu đã dịch.
        return t(`menu.${group}`, group);
    }

    // ==================== PHÂN TÍCH CHẤT LƯỢNG MỘT DÒNG ====================
    // Biến số trong câu là những chỗ hệ thống thay bằng dữ liệu thật khi chạy: "Còn {days} ngày".
    // Dịch mà đánh rơi {days} thì trên màn hình câu đó mất luôn con số, còn gõ sai thành {day} thì
    // chỗ trống không được thay và người dùng đọc thấy đúng chữ "{day}". Đây là lỗi dịch nguy hiểm
    // nhất vì nhìn bản dịch vẫn thấy xuôi tai, chỉ vỡ khi chạy thật.
    const PLACEHOLDER_RE = /\{[^{}\s]*\}/g;

    const placeholderList = (text) => ((text || '').match(PLACEHOLDER_RE) || []).slice().sort();

    const sameList = (a, b) => a.length === b.length && a.every((x, i) => x === b[i]);

    /** Tình trạng của một dòng theo cặp ngôn ngữ đang chọn. */
    function analyze(row) {
        const src = (row.textByLang[state.sourceLang] ?? '').trim();
        const dst = (row.textByLang[state.targetLang] ?? '').trim();

        return {
            src, dst,
            missing: dst === '',
            // Chỉ tính là "giống hệt nguồn" khi có chữ thật: hai ô cùng rỗng là thiếu bản dịch, không
            // phải sao chép nguyên văn. Và câu chỉ gồm biến số ("{name}") thì hai bên giống nhau là đúng.
            same: dst !== '' && dst === src && src.replace(PLACEHOLDER_RE, '').trim() !== '',
            placeholder: dst !== '' && !sameList(placeholderList(src), placeholderList(dst)),
            custom: !row.isSystem,
        };
    }

    // ==================== NẠP DỮ LIỆU ====================
    async function loadLanguageList() {
        try { state.languageList = await API.get('/translation/language'); }
        catch { state.languageList = []; }

        // Chưa nạp bản dịch (CSDL mới) thì vẫn phải dựng được màn hình.
        if (!state.languageList.length)
            state.languageList = [
                { lang: 'vi', label: 'Tiếng Việt', keyCount: 0, isSystem: true, isActive: true },
                { lang: 'en', label: 'English', keyCount: 0, isSystem: true, isActive: true },
            ];

        if (!state.languageList.some(x => x.lang === state.sourceLang))
            state.sourceLang = state.languageList[0].lang;

        // Đích mặc định là ngôn ngữ đầu tiên KHÁC nguồn — mở lên là dịch được ngay.
        if (!state.languageList.some(x => x.lang === state.targetLang))
            state.targetLang = state.languageList.find(x => x.lang !== state.sourceLang)?.lang || state.sourceLang;
    }

    async function loadGroupList() {
        let groupList = [];
        try { groupList = await API.get('/translation/group'); } catch { /* để trống là "tất cả" */ }

        document.getElementById('f-group').innerHTML =
            `<option value="">${esc(t('translation.tatCaManHinh2', 'Tất cả màn hình'))}</option>`
            + groupList.map(g => `<option value="${esc(g.group)}">${esc(groupLabel(g.group))} (${g.keyCount})</option>`).join('');
    }

    /** Tải TOÀN BỘ khóa dịch. Lượt đầu để overlay chạy cho người dùng biết đang tải, các lượt sau im lặng
     *  để không nhấp nháy 5-6 lần liên tiếp. */
    async function loadAllRow() {
        const first = await API.get(`/translation?page=1&pageSize=${FETCH_SIZE}`);
        const rowList = [...first.item];

        const totalPage = Math.ceil(first.totalCount / FETCH_SIZE);
        for (let p = 2; p <= totalPage; p++) {
            const next = await API.getSilent(`/translation?page=${p}&pageSize=${FETCH_SIZE}`);
            rowList.push(...next.item);
        }

        rowList.sort((a, b) => a.resourceKey < b.resourceKey ? -1 : a.resourceKey > b.resourceKey ? 1 : 0);
        state.allRow = rowList;
    }

    // ==================== LỌC ====================
    /** Các dòng khớp mọi bộ lọc TRỪ chip chất lượng — dùng chung cho cả việc đếm số trên từng chip. */
    function baseFilteredRow() {
        const keyword = state.keyword.trim().toLowerCase();

        return state.allRow.filter(row => {
            if (state.group && row.group !== state.group) return false;

            if (state.source === 'system' && !row.isSystem) return false;
            if (state.source === 'custom' && row.isSystem) return false;
            if (state.source === 'customized' && !row.isCustomized) return false;

            if (!keyword) return true;
            if (row.resourceKey.toLowerCase().includes(keyword)) return true;
            return Object.values(row.textByLang).some(x => (x || '').toLowerCase().includes(keyword));
        });
    }

    const QUALITY_TEST = {
        all: () => true,
        missing: (f) => f.missing,
        placeholder: (f) => f.placeholder,
        same: (f) => f.same,
        custom: (f) => f.custom,
    };

    function applyFilter() {
        const base = baseFilteredRow();
        const test = QUALITY_TEST[state.quality] || QUALITY_TEST.all;
        viewRow = base.filter(row => test(analyze(row)));

        const totalPage = Math.max(1, Math.ceil(viewRow.length / PAGE_SIZE));
        state.page = Math.min(Math.max(1, state.page), totalPage);
        pageRow = viewRow.slice((state.page - 1) * PAGE_SIZE, state.page * PAGE_SIZE);

        // Dòng đang chọn rơi ra ngoài kết quả lọc thì chọn lại dòng đầu — để ↑↓ và Enter luôn có đích.
        if (!pageRow.some(x => x.resourceKey === state.selectedKey))
            state.selectedKey = pageRow[0]?.resourceKey ?? null;

        return base;
    }

    // ==================== VẼ GIAO DIỆN ====================
    function renderAll() {
        const base = applyFilter();
        renderLanguageCard();
        renderPair();
        renderChip(base);
        renderHead();
        renderTable();
        renderFoot();
        applyI18n(document.querySelector('.tr-page'));
    }

    /** Số khóa đã có chữ của một ngôn ngữ. Tính tại chỗ thay vì dùng keyCount của API: keyCount đếm số
     *  DÒNG tồn tại, còn cái người dịch cần biết là bao nhiêu dòng đã thật sự có chữ. */
    function translatedCount(lang) {
        return state.allRow.reduce((n, row) => n + ((row.textByLang[lang] ?? '').trim() ? 1 : 0), 0);
    }

    function renderLanguageCard() {
        const total = state.allRow.length;

        const cardHtml = state.languageList.map(lang => {
            const isSource = lang.lang === state.sourceLang;
            const isTarget = lang.lang === state.targetLang;
            const done = translatedCount(lang.lang);
            const percent = total ? Math.round(done * 100 / total) : 0;

            return `
                <button type="button" class="tr-lang ${isTarget ? 'active' : ''} ${isSource ? 'is-source' : ''}"
                        data-lang="${esc(lang.lang)}">
                    <span class="tr-lang-top">
                        ${flagHtml(lang.lang)}
                        <span class="tr-lang-name">${esc(lang.label)}</span>
                        ${isSource
                            ? `<span class="tr-lang-tag">${esc(t('translation.nguon', 'Nguồn'))}</span>`
                            : `<span class="tr-lang-pct">${percent}%</span>`}
                    </span>
                    <span class="tr-bar"><span style="width:${percent}%"></span></span>
                    <span class="tr-lang-count">${done.toLocaleString(Lang.locale)}/${total.toLocaleString(Lang.locale)} ${esc(t('translation.chu', 'câu chữ'))}</span>
                    ${lang.isSystem === false && canDelete
                        ? `<span class="tr-lang-del" role="button" tabindex="0" data-del-lang="${esc(lang.lang)}"
                                 title="${esc(t('translation.xoaNgonNgu', 'Xóa ngôn ngữ này'))}">✕</span>` : ''}
                </button>`;
        }).join('');

        document.getElementById('tr-langs').innerHTML = cardHtml + (canCreate ? `
            <button type="button" class="tr-lang tr-lang-add" id="btn-add-lang">
                <i class="ph ph-plus"></i>
                <span data-i18n="translation.themNgonNgu">Thêm ngôn ngữ</span>
            </button>` : '');
    }

    function renderPair() {
        document.getElementById('pair-source').innerHTML =
            `${flagHtml(state.sourceLang)} ${esc(labelOf(state.sourceLang))}`;
        document.getElementById('pair-target').innerHTML =
            `${flagHtml(state.targetLang)} ${esc(labelOf(state.targetLang))}`;
    }

    function renderChip(base) {
        const flagList = base.map(analyze);
        const count = (key) => flagList.filter(QUALITY_TEST[key]).length;

        const chipList = [
            { key: 'all', label: t('translation.tatCaChip', 'Tất cả'), tone: '', n: base.length },
            { key: 'missing', label: t('translation.thieuBanDich', 'Thiếu bản dịch'), tone: 'tone-warn', n: count('missing') },
            { key: 'placeholder', label: t('translation.lechBienSo', 'Lệch biến số'), tone: 'tone-danger', n: count('placeholder') },
            { key: 'same', label: t('translation.giongHetNguon', 'Giống hệt nguồn'), tone: 'tone-info', n: count('same') },
            { key: 'custom', label: t('translation.tuThem', 'Tự thêm'), tone: '', n: count('custom') },
        ];

        document.getElementById('tr-chips').innerHTML = chipList.map(chip => `
            <button type="button" class="tr-chip ${chip.tone} ${state.quality === chip.key ? 'active' : ''}"
                    data-quality="${chip.key}">
                ${esc(chip.label)} <span class="n">${chip.n.toLocaleString(Lang.locale)}</span>
            </button>`).join('');
    }

    function renderHead() {
        document.getElementById('tr-head').innerHTML = `
            <th class="tr-col-mark"></th>
            <th>${flagHtml(state.sourceLang)}${esc(labelOf(state.sourceLang))}
                <span class="side">(${esc(t('translation.nguonThuong', 'nguồn'))})</span></th>
            <th>${flagHtml(state.targetLang)}${esc(labelOf(state.targetLang))}
                <span class="side">(${esc(t('translation.dichThuong', 'đích'))})</span></th>
            <th class="tr-col-group">${esc(t('translation.manHinh', 'Màn hình'))}</th>
            <th class="tr-col-open"></th>`;
    }

    function markHtml(f) {
        if (f.placeholder) return `<span class="tr-mark warn" title="${esc(t('translation.lechBienSoHint', 'Biến số trong bản dịch không khớp bản nguồn'))}"><i class="ph-fill ph-warning"></i></span>`;
        if (f.missing) return `<span class="tr-mark dot" title="${esc(t('translation.thieuBanDich', 'Thiếu bản dịch'))}"></span>`;
        if (f.same) return `<span class="tr-mark same" title="${esc(t('translation.giongHetNguon', 'Giống hệt nguồn'))}"></span>`;
        return '';
    }

    // Cột "Khóa dịch" cố ý KHÔNG hiện: đó là mã kỹ thuật lập trình viên gắn vào giao diện
    // ("account.banChuaCoChungNhanNao"), người dịch không cần và cũng không sửa được nó — mà nó lại
    // chiếm gần một phần tư bề ngang, đúng phần chỗ mà hai cột câu chữ đang thiếu. Khóa vẫn nằm trong
    // data-row để lưu, hiện ra khi rê chuột lên dòng, và có đủ trong ngăn kéo chi tiết lẫn tệp CSV.
    function renderTable() {
        const tbody = document.getElementById('tbody');

        if (!pageRow.length) {
            tbody.innerHTML = `<tr><td colspan="5">${emptyHtml(t('translation.khongCoKhoaNaoKhop', 'Không có khóa dịch nào khớp điều kiện lọc.'))}</td></tr>`;
            return;
        }

        tbody.innerHTML = pageRow.map(row => {
            const f = analyze(row);
            const selected = row.resourceKey === state.selectedKey;

            return `
                <tr data-row="${esc(row.resourceKey)}" class="${selected ? 'sel' : ''}"
                    title="${esc(row.resourceKey)}">
                    <td class="tr-col-mark">${markHtml(f)}</td>
                    <td class="tr-src">${esc(f.src) || `<span class="tr-empty-cell">—</span>`}</td>
                    <td class="tr-dst"><div class="tr-dst-cell" data-edit-cell="${esc(row.resourceKey)}">${
                        f.dst
                            ? esc(f.dst)
                            : `<span class="tr-empty-cell">${esc(t('translation.chuaDich', 'Chưa dịch'))}</span>`
                    }</div></td>
                    <td class="tr-col-group"><span class="tr-group-chip" title="${esc(row.group)}">${esc(groupLabel(row.group))}</span></td>
                    <td class="tr-col-open">
                        <button type="button" class="tr-open" data-open="${esc(row.resourceKey)}"
                                title="${esc(t('translation.xemChiTiet', 'Xem chi tiết'))}"><i class="ph ph-caret-right"></i></button>
                    </td>
                </tr>`;
        }).join('');
    }

    function renderFoot() {
        const total = viewRow.length;
        const from = total ? (state.page - 1) * PAGE_SIZE + 1 : 0;
        const to = Math.min(state.page * PAGE_SIZE, total);
        const totalPage = Math.max(1, Math.ceil(total / PAGE_SIZE));

        document.getElementById('tr-foot-left').innerHTML = `
            <span>${esc(t('translation.hienThi', 'Hiển thị'))} <b>${from}–${to}</b> / <b>${total.toLocaleString(Lang.locale)}</b> ${esc(t('translation.khoaDichDem', 'khóa dịch'))}</span>
            <span class="tr-dot-sep">·</span>
            <span><kbd class="tr-kbd">↑</kbd> <kbd class="tr-kbd">↓</kbd> ${esc(t('translation.chonDong', 'chọn dòng'))}</span>
            <span class="tr-dot-sep">·</span>
            <span><kbd class="tr-kbd">Enter</kbd> ${esc(t('common.edit', 'Sửa').toLowerCase())}</span>
            <span class="tr-dot-sep">·</span>
            <span><kbd class="tr-kbd">Esc</kbd> ${esc(t('translation.thoat', 'thoát'))}</span>
            <span class="tr-dot-sep">·</span>
            <span><kbd class="tr-kbd">/</kbd> ${esc(t('translation.tim', 'tìm').toLowerCase())}</span>`;

        document.getElementById('tr-pager').innerHTML = `
            <button class="tr-page-btn" id="pg-prev" ${state.page <= 1 ? 'disabled' : ''}
                    aria-label="${esc(t('translation.truoc', 'Trước'))}"><i class="ph ph-caret-left"></i></button>
            <span class="page-of">${state.page} / ${totalPage}</span>
            <button class="tr-page-btn" id="pg-next" ${state.page >= totalPage ? 'disabled' : ''}
                    aria-label="${esc(t('translation.sau', 'Sau'))}"><i class="ph ph-caret-right"></i></button>`;
    }

    // ==================== SỬA NGAY TRÊN BẢNG ====================
    /**
     * Bấm vào ô đích là sửa được luôn, rời ô là tự lưu. Cố ý không có nút "Lưu" trên từng dòng: dịch một
     * màn hình là hàng chục lần sửa liên tiếp, thêm một cú bấm cho mỗi dòng thì phần lớn công sức đổ vào
     * việc bấm nút chứ không phải việc dịch.
     *
     * Esc thì bỏ, không lưu — phải có đường lùi khi lỡ gõ đè lên một bản dịch đang đúng.
     */
    function startEdit(resourceKey) {
        if (!canUpdate) return;

        const row = state.allRow.find(x => x.resourceKey === resourceKey);
        const cell = document.querySelector(`[data-edit-cell="${cssEscape(resourceKey)}"]`);
        if (!row || !cell || state.editingKey === resourceKey) return;

        state.editingKey = resourceKey;
        const original = row.textByLang[state.targetLang] ?? '';

        cell.innerHTML = `
            <textarea class="tr-edit" maxlength="2000"
                      aria-label="${esc(t('translation.banDichSang', 'Bản dịch sang {lang}', { lang: labelOf(state.targetLang) }))}">${esc(original)}</textarea>
            <div class="tr-edit-hint">
                <kbd class="tr-kbd">Enter</kbd> ${esc(t('translation.luu', 'lưu'))}
                <kbd class="tr-kbd">Esc</kbd> ${esc(t('translation.bo', 'bỏ'))}
            </div>`;

        const editor = cell.querySelector('textarea');
        editor.focus();
        editor.setSelectionRange(editor.value.length, editor.value.length);

        let cancelled = false;

        const finish = async () => {
            if (state.editingKey !== resourceKey) return;
            state.editingKey = null;

            const value = editor.value.trim();
            if (cancelled || value === original.trim()) { renderTable(); return; }

            await saveText(row, value);
        };

        editor.addEventListener('blur', finish);
        editor.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') { e.preventDefault(); cancelled = true; editor.blur(); }
            // Enter thường = lưu; Shift+Enter = xuống dòng, vì vài câu dài có ngắt dòng thật.
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); editor.blur(); }
        });
    }

    async function saveText(row, value) {
        try {
            const response = await API.put('/translation', {
                resourceKey: row.resourceKey,
                textByLang: { [state.targetLang]: value },
                // Gửi lại ghi chú cũ: server ghi Note cho mọi dòng nó chạm vào, bỏ trống là xóa mất
                // lý do người trước đã ghi.
                note: row.note || null,
            });

            // Cập nhật ngay trong bộ nhớ thay vì tải lại cả nghìn dòng cho một ô vừa sửa.
            row.textByLang = { ...row.textByLang, [state.targetLang]: value };
            row.isCustomized = row.isSystem
                && Object.entries(row.defaultTextByLang || {}).some(([lang, text]) => text !== row.textByLang[lang]);

            toastOk(response.message);
        } catch (ex) {
            toastErr(ex.message);
        }
        renderAll();
    }

    /** Thoát chuỗi để nhét vào bộ chọn CSS. Khóa dịch có dấu chấm ("menu.course") nên ghép thẳng vào
     *  querySelector sẽ bị hiểu thành bộ chọn class. */
    function cssEscape(value) {
        return window.CSS?.escape ? CSS.escape(value) : String(value).replace(/[^\w-]/g, c => '\\' + c);
    }

    // ==================== NGĂN KÉO CHI TIẾT ====================
    function openDrawer(resourceKey) {
        const row = state.allRow.find(x => x.resourceKey === resourceKey);
        if (!row) return;

        document.getElementById('drawer-key').textContent = row.resourceKey;

        document.getElementById('drawer-body').innerHTML = `
            <div class="tr-meta mb">
                ${row.isSystem
                    ? `<span class="badge blue">${esc(t('translation.cuaHeThong', 'Của hệ thống'))}</span>`
                      + (row.isCustomized ? ` <span class="badge orange">${esc(t('translation.daSuaBadge', 'Đã sửa'))}</span>` : '')
                    : `<span class="badge green">${esc(t('translation.congTyTuThem', 'Công ty tự thêm'))}</span>`}
                <span class="tr-group-chip">${esc(groupLabel(row.group))}</span>
            </div>

            ${state.languageList.map(lang => {
                const text = row.textByLang[lang.lang] ?? '';
                const origin = row.defaultTextByLang?.[lang.lang];
                const changed = origin != null && origin !== text;
                // Nút AI cho từng ngôn ngữ, trừ chính ngôn ngữ nguồn (dịch một câu sang chính nó thì để làm gì).
                const canSuggest = canUpdate && lang.lang !== state.sourceLang;
                return `
                    <div class="tr-field">
                        <div class="tr-field-head">
                            <label for="d-lang-${esc(lang.lang)}">${flagHtml(lang.lang)} ${esc(lang.label)}</label>
                            ${canSuggest ? `<button type="button" class="tr-ai-mini" data-ai-lang="${esc(lang.lang)}"
                                    title="${esc(t('translation.aiGoiYTuNguon', 'Nhờ AI dịch câu này từ {source}', { source: labelOf(state.sourceLang) }))}">
                                <i class="ph ph-sparkle"></i> ${esc(t('translation.aiNhan', 'AI'))}</button>` : ''}
                        </div>
                        <textarea id="d-lang-${esc(lang.lang)}" data-drawer-lang="${esc(lang.lang)}" maxlength="2000"
                                  ${canUpdate ? '' : 'disabled'}>${esc(text)}</textarea>
                        ${changed ? `<div class="tr-origin"><b>${esc(t('translation.banGoc', 'Bản gốc'))}:</b> ${esc(origin)}</div>` : ''}
                    </div>`;
            }).join('')}

            <div class="tr-field">
                <label>${esc(t('translation.ghiChu', 'Ghi chú'))}</label>
                <input id="drawer-note" maxlength="500" value="${esc(row.note || '')}" ${canUpdate ? '' : 'disabled'}
                       placeholder="${esc(t('translation.ghiChuPlaceholder', 'Ghi chú: vì sao chọn cách dịch này'))}">
            </div>

            ${row.updatedByName ? `<div class="tr-meta">${esc(t('translation.suaBoi', 'Sửa gần nhất bởi'))}
                <b>${esc(row.updatedByName)}</b> · ${esc(formatDate(row.updatedAt))}</div>` : ''}`;

        document.getElementById('drawer-foot').innerHTML = `
            ${canUpdate && row.isCustomized ? `<button class="btn ghost" id="drawer-reset">
                <i class="ph ph-arrow-counter-clockwise"></i> ${esc(t('translation.khoiPhuc', 'Khôi phục'))}</button>` : ''}
            ${canDelete && !row.isSystem ? `<button class="btn ghost" id="drawer-del">
                <i class="ph ph-trash"></i> ${esc(t('common.delete', 'Xóa'))}</button>` : ''}
            ${canUpdate ? `<button class="btn tr-primary" id="drawer-save">${esc(t('common.save', 'Lưu'))}</button>` : ''}`;

        document.querySelectorAll('[data-ai-lang]').forEach(button => button.addEventListener('click',
            () => aiSuggestOne(row, button.dataset.aiLang, button)));

        document.getElementById('drawer-save')?.addEventListener('click', () => saveDrawer(row));
        document.getElementById('drawer-reset')?.addEventListener('click', () => resetRow(row));
        document.getElementById('drawer-del')?.addEventListener('click', () => deleteRow(row));

        document.getElementById('drawer').classList.add('open');
        document.getElementById('drawer').setAttribute('aria-hidden', 'false');
        document.getElementById('drawer-mask').classList.add('open');
    }

    function closeDrawer() {
        document.getElementById('drawer').classList.remove('open');
        document.getElementById('drawer').setAttribute('aria-hidden', 'true');
        document.getElementById('drawer-mask').classList.remove('open');
    }

    async function saveDrawer(row) {
        const textByLang = {};
        document.querySelectorAll('[data-drawer-lang]').forEach(el => {
            textByLang[el.dataset.drawerLang] = el.value;
        });
        const note = document.getElementById('drawer-note')?.value.trim() || null;

        try {
            const response = await API.put('/translation', { resourceKey: row.resourceKey, textByLang, note });
            row.textByLang = { ...row.textByLang, ...textByLang };
            row.note = note;
            row.isCustomized = row.isSystem
                && Object.entries(row.defaultTextByLang || {}).some(([lang, text]) => text !== row.textByLang[lang]);
            toastOk(response.message);
            closeDrawer();
            renderAll();
        } catch (ex) { toastErr(ex.message); }
    }

    async function resetRow(row) {
        if (!await confirmDialog(
            t('translation.xacNhanKhoiPhuc', 'Trả "{key}" về đúng câu chữ gốc của hệ thống?', { key: row.resourceKey }))) return;
        try {
            toastOk((await API.put('/translation/reset', { resourceKey: row.resourceKey })).message);
            closeDrawer();
            await reloadAll();
        } catch (ex) { toastErr(ex.message); }
    }

    async function deleteRow(row) {
        if (!await confirmDialog(
            t('translation.xacNhanXoa', 'Xóa khóa "{key}" khỏi bảng dịch?', { key: row.resourceKey }),
            { danger: true, okText: t('common.delete', 'Xóa') })) return;
        try {
            toastOk((await API.del(`/translation?resourceKey=${encodeURIComponent(row.resourceKey)}`)).message);
            closeDrawer();
            await reloadAll();
        } catch (ex) { toastErr(ex.message); }
    }

    async function reloadAll() {
        await Promise.all([loadLanguageList(), loadGroupList()]);
        await loadAllRow();
        renderAll();
    }

    // ==================== CSV ====================
    // Cột: khóa, nhóm, rồi mỗi ngôn ngữ một cột, cuối cùng là ghi chú. Đủ để mở bằng Excel gửi cho bên
    // dịch thuê ngoài, họ điền xong gửi lại là nhập ngược vào được.
    const CSV_FIXED_HEAD = ['key', 'group'];

    function csvCell(value) {
        const text = String(value ?? '');
        // Bọc ngoặc kép khi có dấu phẩy / ngoặc kép / xuống dòng, và nhân đôi ngoặc kép bên trong —
        // đúng quy ước RFC 4180 mà Excel đang dùng.
        return /[",\n\r]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
    }

    function exportCsv() {
        const langList = state.languageList.map(x => x.lang);
        const head = [...CSV_FIXED_HEAD, ...langList, 'note'];

        // Xuất đúng phần đang lọc: người ta lọc ra "còn thiếu bản dịch" rồi mới xuất đi thuê dịch,
        // xuất cả nghìn dòng đã dịch xong kèm theo chỉ tổ làm khó người nhận.
        const bodyList = viewRow.map(row => [
            row.resourceKey,
            row.group,
            ...langList.map(lang => row.textByLang[lang] ?? ''),
            row.note || '',
        ]);

        const csv = [head, ...bodyList].map(line => line.map(csvCell).join(',')).join('\r\n');

        // BOM ở đầu tệp: thiếu nó là Excel trên Windows đọc UTF-8 thành ISO-8859-1, tiếng Việt ra một mớ
        // ký tự lạ ngay khi mở lên.
        // Viết dạng \ufeff chứ không dán ký tự BOM thẳng vào chuỗi: ký tự đó vô hình, ai sửa dòng này
        // cũng có thể xóa mất mà không thấy, và lỗi chỉ lộ ra khi mở tệp bằng Excel.
        const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `ban-dich-${new Date().toISOString().slice(0, 10)}.csv`;
        link.click();
        URL.revokeObjectURL(url);

        toastOk(t('translation.daXuat', 'Đã xuất {count} dòng ra tệp CSV.', { count: bodyList.length }));
    }

    /** Đọc CSV theo RFC 4180: có xử lý ô bọc ngoặc kép, dấu phẩy và xuống dòng nằm trong ô. */
    function parseCsv(text) {
        const rowList = [];
        let cellList = [], cell = '', inQuote = false;

        // Bỏ BOM nếu tệp do Excel xuất ra.
        if (text.charCodeAt(0) === 0xfeff) text = text.slice(1);

        for (let i = 0; i < text.length; i++) {
            const c = text[i];

            if (inQuote) {
                if (c === '"') {
                    if (text[i + 1] === '"') { cell += '"'; i++; }
                    else inQuote = false;
                } else cell += c;
                continue;
            }

            if (c === '"') { inQuote = true; continue; }
            if (c === ',') { cellList.push(cell); cell = ''; continue; }
            if (c === '\r') continue;
            if (c === '\n') { cellList.push(cell); rowList.push(cellList); cellList = []; cell = ''; continue; }
            cell += c;
        }
        if (cell !== '' || cellList.length) { cellList.push(cell); rowList.push(cellList); }

        return rowList.filter(line => line.some(x => x.trim() !== ''));
    }

    async function importCsv(file) {
        const lineList = parseCsv(await file.text());
        if (lineList.length < 2) { toastErr(t('translation.csvRong', 'Tệp CSV không có dòng dữ liệu nào.')); return; }

        const head = lineList[0].map(x => x.trim().toLowerCase());
        const keyIndex = head.indexOf('key');
        if (keyIndex < 0) { toastErr(t('translation.csvThieuCotKey', 'Tệp CSV phải có cột "key".')); return; }

        const noteIndex = head.indexOf('note');
        const langIndexList = state.languageList
            .map(x => ({ lang: x.lang, index: head.indexOf(x.lang) }))
            .filter(x => x.index >= 0);

        if (!langIndexList.length) {
            toastErr(t('translation.csvThieuCotNgonNgu', 'Tệp CSV không có cột ngôn ngữ nào khớp hệ thống (vd "vi", "en").'));
            return;
        }

        const keyByRow = new Map(state.allRow.map(x => [x.resourceKey, x]));
        let updated = 0, skipped = 0, failed = 0;

        for (const line of lineList.slice(1)) {
            const resourceKey = (line[keyIndex] || '').trim();
            const row = keyByRow.get(resourceKey);
            // Khóa không có trong hệ thống thì BỎ QUA chứ không tự tạo: nhầm một ô ở cột key là sinh ra
            // cả loạt khóa rác mà không ai gỡ được nữa.
            if (!resourceKey || !row) { skipped++; continue; }

            const textByLang = {};
            for (const { lang, index } of langIndexList) {
                const text = (line[index] ?? '').trim();
                if (text && text !== (row.textByLang[lang] ?? '').trim()) textByLang[lang] = text;
            }

            const note = noteIndex >= 0 ? ((line[noteIndex] || '').trim() || null) : (row.note || null);
            if (!Object.keys(textByLang).length && note === (row.note || null)) { skipped++; continue; }

            try {
                // Im lặng: cả trăm dòng mà mỗi dòng bật overlay một lần thì màn hình chỉ toàn nhấp nháy.
                await API.putSilent('/translation', { resourceKey, textByLang, note });
                Object.assign(row.textByLang, textByLang);
                row.note = note;
                updated++;
            } catch { failed++; }
        }

        await reloadAll();
        toastOk(t('translation.ketQuaNhap', 'Nhập xong: cập nhật {updated} dòng, bỏ qua {skipped}, lỗi {failed}.',
            { updated, skipped, failed }));
    }

    // ==================== THÊM NGÔN NGỮ ====================
    /** Các ngôn ngữ gợi ý sẵn, để người dùng chọn từ danh sách thay vì phải tự tra mã ISO và thẻ locale.
     *
     *  Xếp các nước có nhiều lao động/đối tác với doanh nghiệp Việt lên đầu. Đây chỉ là DANH SÁCH GỢI Ý —
     *  ngôn ngữ nào không có ở đây vẫn thêm được bằng mục "Ngôn ngữ khác". */
    const LANGUAGE_PRESET_LIST = [
        { code: 'en', name: 'English',           vi: 'Tiếng Anh',            locale: 'en-US' },
        { code: 'ja', name: '日本語',             vi: 'Tiếng Nhật',           locale: 'ja-JP' },
        { code: 'ko', name: '한국어',             vi: 'Tiếng Hàn',            locale: 'ko-KR' },
        { code: 'zh', name: '中文',               vi: 'Tiếng Trung',          locale: 'zh-CN' },
        { code: 'th', name: 'ไทย',                vi: 'Tiếng Thái',           locale: 'th-TH' },
        { code: 'km', name: 'ខ្មែរ',                vi: 'Tiếng Khmer',          locale: 'km-KH' },
        { code: 'lo', name: 'ລາວ',                vi: 'Tiếng Lào',            locale: 'lo-LA' },
        { code: 'id', name: 'Bahasa Indonesia',  vi: 'Tiếng Indonesia',      locale: 'id-ID' },
        { code: 'ms', name: 'Bahasa Melayu',     vi: 'Tiếng Mã Lai',         locale: 'ms-MY' },
        { code: 'tl', name: 'Filipino',          vi: 'Tiếng Philippines',    locale: 'fil-PH' },
        { code: 'my', name: 'မြန်မာ',              vi: 'Tiếng Miến Điện',      locale: 'my-MM' },
        { code: 'hi', name: 'हिन्दी',                vi: 'Tiếng Hindi',          locale: 'hi-IN' },
        { code: 'fr', name: 'Français',          vi: 'Tiếng Pháp',           locale: 'fr-FR' },
        { code: 'de', name: 'Deutsch',           vi: 'Tiếng Đức',            locale: 'de-DE' },
        { code: 'es', name: 'Español',           vi: 'Tiếng Tây Ban Nha',    locale: 'es-ES' },
        { code: 'pt', name: 'Português',         vi: 'Tiếng Bồ Đào Nha',     locale: 'pt-BR' },
        { code: 'it', name: 'Italiano',          vi: 'Tiếng Ý',              locale: 'it-IT' },
        { code: 'nl', name: 'Nederlands',        vi: 'Tiếng Hà Lan',         locale: 'nl-NL' },
        { code: 'ru', name: 'Русский',           vi: 'Tiếng Nga',            locale: 'ru-RU' },
        { code: 'ar', name: 'العربية',             vi: 'Tiếng Ả Rập',          locale: 'ar-SA' },
    ];

    const OTHER_LANGUAGE_VALUE = '__other__';

    function openLanguageForm() {
        const existingCodeList = state.languageList.map(x => x.lang);
        const availableList = LANGUAGE_PRESET_LIST.filter(x => !existingCodeList.includes(x.code));

        document.getElementById('l-preset').innerHTML =
            `<option value="">${t('translation.chonMotNgonNgu', '— Chọn một ngôn ngữ —')}</option>`
            + availableList.map(x => `<option value="${esc(x.code)}">${esc(x.vi)} — ${esc(x.name)}</option>`).join('')
            + `<option value="${OTHER_LANGUAGE_VALUE}">${t('translation.ngonNguKhac', 'Ngôn ngữ khác (tự nhập)')}</option>`;

        document.getElementById('l-code').value = '';
        document.getElementById('l-name').value = '';
        document.getElementById('l-locale').value = '';
        applyLanguagePreset();

        document.getElementById('l-copy').innerHTML = state.languageList
            .map(x => `<option value="${esc(x.lang)}" ${x.lang === state.sourceLang ? 'selected' : ''}>`
                + `${t('translation.chepTu', 'Chép từ')} ${esc(x.label)}</option>`).join('')
            + `<option value="">${t('translation.khongChep', 'Để trống, tự nhập từ đầu')}</option>`;

        openModal('modal-lang');
    }

    /** Chọn một ngôn ngữ trong danh sách gợi ý thì tự điền hộ mã và kiểu hiển thị ngày/số, và giấu hai ô đó
     *  đi — người dùng chỉ cần xác nhận tên gọi. Chọn "Ngôn ngữ khác" thì mở cả hai ô ra để tự nhập. */
    function applyLanguagePreset() {
        const value = document.getElementById('l-preset').value;
        const isOther = value === OTHER_LANGUAGE_VALUE;
        const preset = LANGUAGE_PRESET_LIST.find(x => x.code === value);

        document.getElementById('l-code-field').classList.toggle('hidden', !isOther);
        document.getElementById('l-locale-field').classList.toggle('hidden', !isOther);
        document.getElementById('l-code').required = isOther;

        if (preset) {
            document.getElementById('l-code').value = preset.code;
            document.getElementById('l-name').value = preset.name;
            document.getElementById('l-locale').value = preset.locale;
        } else if (isOther) {
            document.getElementById('l-code').value = '';
            document.getElementById('l-name').value = '';
            document.getElementById('l-locale').value = '';
        }
    }

    async function saveNewLanguage(e) {
        e.preventDefault();

        const body = {
            code: document.getElementById('l-code').value.trim().toLowerCase(),
            name: document.getElementById('l-name').value.trim(),
            localeTag: document.getElementById('l-locale').value.trim() || null,
            copyFromLang: document.getElementById('l-copy').value || null,
        };

        // Chưa chọn gì ở ô đầu thì mã còn rỗng. Nói thẳng vào việc phải làm, thay vì để server trả về lỗi
        // "mã ngôn ngữ chỉ gồm 2-10 chữ cái" — người dùng có nhìn thấy ô mã nào đâu mà hiểu.
        if (!body.code) {
            toastErr(t('translation.chuaChonNgonNgu', 'Hãy chọn một ngôn ngữ trong danh sách.'));
            return;
        }

        try {
            toastOk((await API.post('/translation/language', body)).message);
            closeModal('modal-lang');
            state.targetLang = body.code;      // vừa thêm xong thì bắt tay dịch luôn ngôn ngữ đó
            await reloadAll();
        } catch (ex) { toastErr(ex.message); }
    }

    async function deleteLanguage(code) {
        const language = state.languageList.find(x => x.lang === code);
        const label = language?.label || code;
        const keyCount = translatedCount(code).toLocaleString(Lang.locale);

        if (!await confirmDialog(
            t('translation.xacNhanXoaNgonNgu', 'Xóa ngôn ngữ "{label}"? Toàn bộ {count} câu chữ đã dịch cho ngôn ngữ này sẽ mất và không khôi phục được.',
                { label, count: keyCount }),
            { danger: true, okText: t('common.delete', 'Xóa') })) return;

        try {
            toastOk((await API.del(`/translation/language/${encodeURIComponent(code)}`)).message);
            if (state.targetLang === code) state.targetLang = '';
            await reloadAll();
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== THÊM THUẬT NGỮ ====================
    function openAddForm() {
        document.getElementById('a-key').value = '';
        document.getElementById('a-note').value = '';
        document.getElementById('a-lang-box').innerHTML = state.languageList.map(lang => `
            <div class="tr-field">
                <label for="a-lang-${esc(lang.lang)}">${flagHtml(lang.lang)} ${esc(lang.label)}</label>
                <textarea id="a-lang-${esc(lang.lang)}" data-add-lang="${esc(lang.lang)}" maxlength="2000"></textarea>
            </div>`).join('');
        openModal('modal-add');
    }

    async function saveNewKey(e) {
        e.preventDefault();
        const resourceKey = document.getElementById('a-key').value.trim();
        const textByLang = {};
        document.querySelectorAll('[data-add-lang]').forEach(el => {
            if (el.value.trim()) textByLang[el.dataset.addLang] = el.value.trim();
        });

        if (!Object.keys(textByLang).length) {
            toastErr(t('translation.nhapItNhatMotNgonNgu', 'Hãy nhập câu chữ cho ít nhất một ngôn ngữ.'));
            return;
        }

        try {
            const response = await API.post('/translation', {
                resourceKey, textByLang, note: document.getElementById('a-note').value.trim() || null
            });
            toastOk(response.message);
            closeModal('modal-add');
            state.keyword = resourceKey;
            document.getElementById('f-keyword').value = resourceKey;
            await reloadAll();
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== DỊCH BẰNG AI ====================
    //
    // AI dịch NHÁP, người dịch soát lại — không phải "bấm một nút là xong tiếng Nhật". Vì vậy mặc định chỉ
    // điền vào những ô CÒN TRỐNG (không đụng tới câu người thật đã sửa), và mọi dòng máy dịch đều bị server
    // gắn ghi chú "cần soát lại" để về sau còn phân biệt được với bản đã có người đọc qua.
    //
    // Cắt lô rồi gọi nhiều lượt thay vì ném cả nghìn khóa vào một yêu cầu: xem MaxAiBatchKey bên
    // Controllers/TranslationController.cs. Con số dưới đây phải ≤ trần đó.
    const AI_BATCH_SIZE = 40;
    const AI_NOTE = 'Dịch tự động bằng AI — cần soát lại';

    const aiRun = { running: false, cancelled: false };

    /** Những dòng sẽ được dịch: đúng phần người dùng ĐANG THẤY sau bộ lọc mà còn thiếu bản dịch. Lấy theo
     *  bộ lọc chứ không phải cả bảng để "dịch nốt màn hình Báo cáo" là việc làm được — chọn nhóm màn hình
     *  ở ô lọc rồi bấm dịch, thay vì buộc phải dịch cả nghìn dòng của toàn hệ thống trong một lần. */
    const aiCandidateList = () => viewRow.filter(row => analyze(row).missing);

    function showAiProgress(done, total) {
        const bar = document.getElementById('tr-ai-bar');
        bar.hidden = false;
        document.getElementById('tr-ai-text').textContent =
            t('translation.aiDangDich', 'Đang dịch bằng AI: {done}/{total} câu', { done, total });
        document.getElementById('tr-ai-fill').style.width = total ? `${Math.round(done * 100 / total)}%` : '0%';
    }

    const hideAiProgress = () => { document.getElementById('tr-ai-bar').hidden = true; };

    /** Ghi bản dịch máy vào bộ nhớ của màn hình, y như khi người dùng tự gõ — server đã lưu rồi, tải lại cả
     *  nghìn dòng sau MỖI lô chỉ để đọc lại đúng những câu vừa gửi đi là phí. */
    function applyAiText(resourceKey, text) {
        const row = state.allRow.find(x => x.resourceKey === resourceKey);
        if (!row) return;
        row.textByLang = { ...row.textByLang, [state.targetLang]: text };
        if (!row.note) row.note = AI_NOTE;
        row.isCustomized = row.isSystem
            && Object.entries(row.defaultTextByLang || {}).some(([lang, value]) => value !== row.textByLang[lang]);
    }

    async function runAiTranslate() {
        if (aiRun.running) return;
        if (state.sourceLang === state.targetLang) {
            toastErr(t('translation.aiTrungNgonNgu', 'Ngôn ngữ nguồn và đích đang trùng nhau — chọn ngôn ngữ đích khác trước.'));
            return;
        }

        const candidateList = aiCandidateList();
        if (!candidateList.length) {
            toastErr(t('translation.aiKhongCoDongThieu',
                'Không có dòng nào đang thiếu bản dịch trong bộ lọc hiện tại.'));
            return;
        }

        const confirmed = await confirmDialog(
            t('translation.aiXacNhan',
                'Dùng AI dịch {count} câu còn thiếu, từ {source} sang {target}? Đây là bản dịch nháp: hệ thống sẽ gắn ghi chú "cần soát lại" cho từng dòng, và không đụng tới những câu đã có người dịch.',
                { count: candidateList.length, source: labelOf(state.sourceLang), target: labelOf(state.targetLang) }),
            { okText: t('translation.aiDichNgay', 'Dịch bằng AI') });
        if (!confirmed) return;

        aiRun.running = true;
        aiRun.cancelled = false;
        showAiProgress(0, candidateList.length);

        let translatedCount = 0;
        let failedCount = 0;
        let stoppedByError = false;

        try {
            for (let from = 0; from < candidateList.length; from += AI_BATCH_SIZE) {
                if (aiRun.cancelled) break;

                const batch = candidateList.slice(from, from + AI_BATCH_SIZE);
                let response;
                try {
                    response = await API.put('/translation/ai-translate', {
                        sourceLang: state.sourceLang,
                        targetLang: state.targetLang,
                        resourceKeyList: batch.map(x => x.resourceKey),
                        overwrite: false,
                        preview: false,
                    });
                } catch (ex) {
                    // Dừng hẳn chứ không bỏ qua lô hỏng rồi chạy tiếp: lỗi ở đây gần như luôn là thiếu khóa
                    // API, hết hạn mức hoặc mất mạng — lô sau sẽ hỏng y hệt, chạy tiếp chỉ tốn thêm thời gian
                    // và dội ra hàng chục thông báo lỗi giống nhau.
                    toastErr(ex.message);
                    stoppedByError = true;
                    break;
                }

                Object.entries(response.textByKey || {}).forEach(([key, text]) => applyAiText(key, text));
                translatedCount += response.translatedCount || 0;
                failedCount += (response.failedKeyList || []).length;

                showAiProgress(Math.min(from + batch.length, candidateList.length), candidateList.length);
                renderAll();
            }
        } finally {
            aiRun.running = false;
            hideAiProgress();
        }

        renderAll();

        if (translatedCount === 0 && !stoppedByError) {
            toastErr(t('translation.aiKhongDichDuoc', 'AI không trả về câu dịch nào.'));
            return;
        }
        if (translatedCount === 0) return;   // lỗi đã báo ở trên rồi

        let message = aiRun.cancelled
            ? t('translation.aiDaDung', 'Đã dừng. Dịch được {count} câu.', { count: translatedCount })
            : t('translation.aiXong', 'Đã dịch {count} câu bằng AI. Hãy soát lại trước khi dùng chính thức.',
                { count: translatedCount });
        if (failedCount > 0)
            message += ' ' + t('translation.aiConThieu', 'Còn {count} câu AI không trả về, cần dịch tay.', { count: failedCount });
        toastOk(message);
    }

    /** Gợi ý bản dịch cho ĐÚNG một dòng trong ngăn kéo chi tiết. Khác bản dịch hàng loạt ở chỗ nó KHÔNG ghi
     *  vào cơ sở dữ liệu (preview): người dịch đang mở biểu mẫu sửa, chữ phải rơi vào ô để họ đọc lại và
     *  chỉnh, rồi mới bấm Lưu — ghi thẳng sau lưng thì mấy ô còn lại họ đang gõ dở cũng bị lệch theo. */
    async function aiSuggestOne(row, lang, button) {
        const editor = document.getElementById(`d-lang-${lang}`);
        if (!editor) return;

        const original = button.innerHTML;
        button.disabled = true;
        button.innerHTML = '<span class="spinner-inline"></span>';

        try {
            const response = await API.put('/translation/ai-translate', {
                sourceLang: state.sourceLang,
                targetLang: lang,
                resourceKeyList: [row.resourceKey],
                // Người dùng bấm đúng vào ô này để xin bản dịch mới, nên có sẵn chữ vẫn cứ dịch — chữ cũ chưa
                // mất đi đâu cả, nó chỉ nằm trong ô cho tới khi bấm Lưu.
                overwrite: true,
                preview: true,
            });

            const text = response.textByKey?.[row.resourceKey];
            if (!text) {
                toastErr(t('translation.aiKhongDichDuoc', 'AI không trả về câu dịch nào.'));
                return;
            }

            editor.value = text;
            editor.focus();
            toastOk(t('translation.aiDaGoiY', 'AI đã điền bản dịch — đọc lại rồi bấm Lưu.'));
        } catch (ex) {
            toastErr(ex.message);
        } finally {
            button.disabled = false;
            button.innerHTML = original;
        }
    }

    // ==================== SỰ KIỆN ====================
    const rerender = () => { state.page = 1; renderAll(); };

    document.getElementById('f-keyword').addEventListener('input', (e) => {
        state.keyword = e.target.value;
        rerender();
    });
    document.getElementById('f-group').addEventListener('change', (e) => { state.group = e.target.value; rerender(); });
    document.getElementById('f-source').addEventListener('change', (e) => { state.source = e.target.value; rerender(); });

    // Ủy quyền sự kiện cho cả vùng: mọi khối bên dưới đều được vẽ lại liên tục, gắn trực tiếp lên từng
    // nút thì sau mỗi lần vẽ phải gắn lại hết.
    document.querySelector('.tr-page').addEventListener('click', (e) => {
        const chip = e.target.closest('[data-quality]');
        if (chip) { state.quality = chip.dataset.quality; rerender(); return; }

        const delLang = e.target.closest('[data-del-lang]');
        if (delLang) { e.stopPropagation(); deleteLanguage(delLang.dataset.delLang); return; }

        if (e.target.closest('#btn-add-lang')) { openLanguageForm(); return; }

        const langCard = e.target.closest('[data-lang]');
        if (langCard) {
            const lang = langCard.dataset.lang;
            if (lang !== state.sourceLang) { state.targetLang = lang; state.page = 1; renderAll(); }
            return;
        }

        const open = e.target.closest('[data-open]');
        if (open) { openDrawer(open.dataset.open); return; }

        const cell = e.target.closest('[data-edit-cell]');
        if (cell) {
            state.selectedKey = cell.dataset.editCell;
            renderTable();
            startEdit(cell.dataset.editCell);
            return;
        }

        const tr = e.target.closest('tr[data-row]');
        if (tr) { state.selectedKey = tr.dataset.row; renderTable(); }
    });

    document.getElementById('tr-pager').addEventListener('click', (e) => {
        if (e.target.closest('#pg-prev')) { state.page--; renderAll(); }
        if (e.target.closest('#pg-next')) { state.page++; renderAll(); }
    });

    // Đổi chiều cặp ngôn ngữ: bấm vào viên "Nguồn" là hoán đổi nguồn ↔ đích. Người dịch hay phải soi
    // ngược lại xem câu tiếng Anh đang có nghĩa gì so với tiếng Việt.
    document.getElementById('pair-source').addEventListener('click', swapPair);
    document.getElementById('pair-target').addEventListener('click', swapPair);

    function swapPair() {
        const source = state.sourceLang;
        state.sourceLang = state.targetLang;
        state.targetLang = source;
        state.page = 1;
        renderAll();
    }

    document.getElementById('btn-add')?.addEventListener('click', openAddForm);
    document.getElementById('add-form').addEventListener('submit', saveNewKey);
    document.getElementById('lang-form').addEventListener('submit', saveNewLanguage);
    document.getElementById('l-preset').addEventListener('change', applyLanguagePreset);

    document.getElementById('btn-ai')?.addEventListener('click', runAiTranslate);
    document.getElementById('tr-ai-stop').addEventListener('click', () => { aiRun.cancelled = true; });

    bindPopMenu(document.getElementById('btn-more'), () => [
        ...(canUpdate ? [{
            icon: 'ph ph-upload-simple',
            label: t('translation.nhapCsv', 'Nhập CSV'),
            onClick: () => document.getElementById('csv-file').click()
        }] : []),
        {
            icon: 'ph ph-download-simple',
            label: t('translation.xuatCsv', 'Xuất CSV'),
            onClick: exportCsv
        }
    ]);
    document.getElementById('csv-file').addEventListener('change', async (e) => {
        const file = e.target.files[0];
        // Xóa lựa chọn để chọn lại ĐÚNG tệp vừa rồi vẫn kích hoạt sự kiện change.
        e.target.value = '';
        if (file) await importCsv(file);
    });

    document.getElementById('drawer-close').addEventListener('click', closeDrawer);
    document.getElementById('drawer-mask').addEventListener('click', closeDrawer);

    // ==================== PHÍM TẮT ====================
    // Dịch cả nghìn dòng thì mỗi lần rời tay khỏi bàn phím để với con chuột đều là thời gian mất đi.
    document.addEventListener('keydown', (e) => {
        const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName);

        if (e.key === '/' && !typing) {
            e.preventDefault();
            document.getElementById('f-keyword').focus();
            document.getElementById('f-keyword').select();
            return;
        }

        if (e.key === 'Escape' && !typing) {
            if (document.getElementById('drawer').classList.contains('open')) closeDrawer();
            else { document.activeElement?.blur?.(); }
            return;
        }

        if (typing || state.editingKey) return;
        if (document.querySelector('.modal-mask.open')) return;

        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            moveSelection(e.key === 'ArrowDown' ? 1 : -1);
            return;
        }

        if (e.key === 'Enter' && state.selectedKey) {
            e.preventDefault();
            startEdit(state.selectedKey);
        }
    });

    /** Di chuyển dòng đang chọn. Tới cuối trang thì sang trang kế tiếp — danh sách một nghìn dòng mà
     *  cứ 25 dòng lại phải với chuột bấm mũi tên thì phím tắt gần như vô dụng. */
    function moveSelection(step) {
        const index = pageRow.findIndex(x => x.resourceKey === state.selectedKey);
        const next = index + step;

        if (next >= 0 && next < pageRow.length) {
            state.selectedKey = pageRow[next].resourceKey;
            renderTable();
            document.querySelector(`tr[data-row="${cssEscape(state.selectedKey)}"]`)
                ?.scrollIntoView({ block: 'nearest' });
            return;
        }

        const totalPage = Math.max(1, Math.ceil(viewRow.length / PAGE_SIZE));
        if (step > 0 && state.page < totalPage) {
            state.page++;
            state.selectedKey = null;
            renderAll();
        } else if (step < 0 && state.page > 1) {
            state.page--;
            renderAll();
            state.selectedKey = pageRow.at(-1)?.resourceKey ?? null;
            renderTable();
        }
    }

    // ==================== KHỞI ĐỘNG ====================
    await Promise.all([loadLanguageList(), loadGroupList()]);
    await loadAllRow();
    renderAll();
})();
