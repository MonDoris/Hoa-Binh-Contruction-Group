// File: wwwroot/pages/certificate/certificate.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    if (!Auth.require()) return;
    mountLayout('certificate', t('index.stat-cert', 'Chứng nhận'), t('certificate.lichSuChungNhanHoanThanhDaoTao', 'Lịch sử chứng nhận hoàn thành đào tạo'));

    const content = document.getElementById('content');
    content.innerHTML = skeletonHtml('cards', { count: 6, thumb: false });

    // ----- Trạng thái của các thẻ chứng nhận bên ngoài / duyệt / in danh sách -----
    // Khai báo ở ĐÂY, trước lượt dựng trang bên dưới: các hàm dựng thẻ đọc chúng ngay trong lượt chạy đầu, mà
    // một "let" khai báo phía sau thì lúc đó vẫn nằm trong Temporal Dead Zone.
    /** Thẻ nào đã tải dữ liệu lần đầu — tải lười, chỉ khi người dùng mở thẻ. */
    const loadedTab = { mine: false, external: false, review: false, register: false };
    /** Chứng nhận nội bộ của CHÍNH người xem — chỉ tải cho thẻ "Chứng nhận của tôi" (xem hasMineTab). */
    let myCertList = [];
    let myExternalList = [];
    let reviewList = [];
    /** id -> ExternalCertificateDto, gom từ cả "của tôi" lẫn hàng duyệt, để nút Xem tìm lại được giấy. */
    const externalById = new Map();
    /** Toàn bộ phòng ban (bộ lọc của Admin) — khác departmentList bên dưới vốn chỉ là phòng ban cấp dưới. */
    let allDepartmentList = null;
    let registerRowList = [];
    /** Hộp thoại "Thêm chứng nhận": cách lấy tệp đang chọn, luồng camera, khung ngắm, ảnh đã chụp, tệp đã chọn. */
    let scanMode = 'camera';
    let cameraStream = null;
    let guideLandscape = true;
    let capturedCanvas = null;
    let enhanceCapture = false;
    let pickedFile = null;
    let pickedFileUrl = null;
    let viewFileUrl = null;
    /** AI đọc giấy trong hộp thoại "Thêm": tệp đang/đã được đọc — ĐÚNG dãy byte sẽ gửi đi, vì máy chủ nhận ra kết
     *  quả đọc sẵn theo dấu vân tay tệp —, bộ hủy lượt đọc đang chạy, trạng thái, và giá trị AI đã điền vào từng ô
     *  (ô nhân viên tự sửa thì lượt đọc sau không đè). */
    let scanFile = null;
    let scanSeq = 0;
    let scanController = null;
    let scanState = 'idle';
    let scanTimer = null;
    const aiFilledValue = {};
    /** Hẹn giờ tải lại danh sách "của tôi" khi còn giấy AI chưa đối chiếu xong. */
    let myRefreshTimer = null;
    /** Khớp với ExternalCertificateService (máy chủ còn kiểm lại theo chữ ký đầu tệp). */
    const acceptedTypeList = ['application/pdf', 'image/jpeg', 'image/png', 'image/webp'];
    const maxFileByte = 15 * 1024 * 1024;

    let list = [];
    let departmentList = [];
    let subordinateList = [];
    /** Người xem thuộc phòng Nhân sự: có thẻ duyệt chứng nhận bên ngoài và nút "Phòng nhân sự xác nhận". Admin có
     *  thẻ duyệt riêng nên không hỏi. */
    let isHrReviewer = false;
    try {
        // Gọi song song: hai danh sách không phụ thuộc nhau, gọi nối tiếp thì trang chờ đúng hai vòng mạng.
        // Riêng nhánh cấp dưới tự nuốt lỗi và trả về mảng rỗng — vai trò không quản ai vẫn phải mở được
        // trang chứng nhận của chính mình, không thể để nó kéo cả trang xuống một màn hình báo lỗi.
        //
        // Bậc lọc thứ nhất (phòng ban) được tải TRƯỚC danh sách tên. Với Admin, tải tên ngay từ đầu nghĩa
        // là kéo về 100 cái tên rải khắp công ty mà người ta gần như chắc chắn sẽ lọc lại ngay sau đó.
        [list, departmentList, isHrReviewer] = await Promise.all([
            Auth.isManager ? API.get('/certificate') : API.get('/certificate/my'),
            API.getSilent('/certificate/subordinate-department').catch(() => []),
            Auth.isAdmin ? false
                : API.getSilent('/externalcertificate/hr-access').then(x => !!x?.isHrReviewer).catch(() => false)
        ]);
    } catch (ex) {
        content.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        return;
    }

    /** Chứng nhận đã tải về của từng người: userId -> mảng CertificateDto. Giữ lại để lần xổ thứ hai của
     *  cùng một người là tức thì — người quản lý hay đóng mở qua lại giữa vài nhân viên. */
    const certByUser = new Map();
    /** id chứng nhận -> DTO, để nút "Xem" trong khung xổ ra tìm lại được thẻ mà không phải dò từng mảng. */
    const certById = new Map();
    /** userId -> thông tin nhân viên. Tích lũy dần chứ không đọc thẳng từ subordinateList: danh sách đó bị
     *  thay mới sau mỗi lượt tìm kiếm, mà người đang được chọn có thể không còn khớp từ khóa mới — lúc đó
     *  dòng tên ở đầu khung kết quả sẽ trống trơn. */
    const personById = new Map();
    subordinateList.forEach(u => personById.set(u.userId, u));
    /** Nhân viên đang được chọn trên thanh tab, null = chưa chọn ai. */
    let selectedUserId = null;
    /** Phòng ban đang được chọn ở bậc lọc thứ nhất, null = chưa chọn. */
    let selectedDepartmentId = null;
    /** Toàn bộ người của phòng ban đang chọn (CHƯA lọc theo từ khóa) — dải số liệu tính trên danh sách này, để
     *  gõ tìm một cái tên không làm các con số tổng quan nhảy theo. */
    let deptPeopleList = [];
    /** Lọc nhanh danh sách bên trái: 'all' | 'has' (đã có chứng nhận) | 'none' (chưa có). */
    let peopleFilter = 'all';
    /** Bộ lọc của danh sách chứng nhận toàn công ty (Chuyên viên đào tạo, Giảng viên): phòng ban -> nhân viên
     *  -> từ khóa, lọc ngay trên trình duyệt vì cả danh sách đã nằm sẵn trong `list`. '' = không lọc. */
    const allCertFilter = { departmentKey: '', userId: '', keyword: '' };
    /** Phòng ban người xem đã tự bấm mở — mặc định thu gọn hết, và giữ nguyên lựa chọn qua các lượt vẽ lại. */
    const openDepartmentKey = new Set();
    /** Đúng một phòng ban thì bỏ hẳn bậc lọc đó đi — bắt người ta bấm một lựa chọn duy nhất là vô nghĩa. */
    const singleDepartment = departmentList.length === 1;
    /** Thẻ "Chứng nhận nội bộ" chỉ là chứng nhận của chính mình khi người xem là học viên thường. Ai có cấp dưới
     *  hoặc xem được chứng nhận cả công ty thì thẻ đó dành cho người khác — họ cũng đi học, nên cần thẻ riêng.
     *  Admin chỉ quản lý, không đi học — không có thẻ này. */
    const hasMineTab = !Auth.isAdmin && (departmentList.length > 0 || Auth.isManager);
    /** Cấp trưởng (Trưởng phòng, Giám Đốc, Tổng Giám Đốc) in được danh sách chứng nhận của người thuộc quyền mình —
     *  khớp RoleName.CertificateRegisterHeads; máy chủ tự giới hạn phạm vi. Admin có sẵn thẻ này. */
    const isHeadPrinter = !Auth.isAdmin && departmentList.length > 0
        && ['Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'].some(role => Auth.hasRole(role));
    /** Thẻ "In danh sách" của người không phải Admin: phòng Nhân sự (in cả công ty) hoặc cấp trưởng (phạm vi mình quản). */
    const hasRegisterTab = !Auth.isAdmin && (isHrReviewer || isHeadPrinter);
    /** Giá trị "Tất cả phòng ban" của bậc lọc thứ nhất (Admin / Tổng Giám Đốc nhìn nhiều phòng). Id phòng ban thật
     *  từ 1 trở lên, 0 là "Chưa gắn phòng ban" — nên -1 không đụng ai. */
    const ALL_DEPARTMENT = -1;
    // Cả danh sách chính cũng vào chung bộ nhớ đệm: nút "Xem chi tiết" giờ tra theo id chứng nhận chứ
    // không còn theo vị trí trong mảng — một chỉ số vốn sai ngay khi trang có thêm khối thứ hai cũng vẽ thẻ.
    list.forEach(c => certById.set(c.id, c));

    // Chỉ một phòng ban thì chọn sẵn và tải luôn danh sách tên, để Trưởng phòng / Giám Đốc thấy đúng thứ
    // họ vẫn thấy: bấm một cái là ra chứng nhận, không thêm bước nào.
    if (singleDepartment) {
        selectedDepartmentId = departmentList[0].departmentId;
        subordinateList = await API.getSilent(
            `/certificate/subordinate?departmentId=${selectedDepartmentId}`).catch(() => []);
        subordinateList.forEach(u => personById.set(u.userId, u));
        deptPeopleList = subordinateList;
    }

    // Có khối nhân viên cấp dưới thì bỏ hẳn danh sách "N chứng nhận đã được cấp" bên dưới: chứng nhận của
    // từng người đã xem được qua hai ô chọn ở trên, để thêm một danh sách dài nữa chỉ là lặp lại. Không có
    // khối đó (nhân viên thường) thì danh sách này là thứ DUY NHẤT trên trang, nên vẫn giữ.
    // Vai trò xem được chứng nhận CẢ công ty (Chuyên viên đào tạo...) thì danh sách đó gồm người của mọi phòng
    // ban — xếp theo phòng ban -> người và có bộ lọc, đổ phẳng ra thì không biết thẻ nào của ai.
    const internalHtml = departmentList.length ? subordinateSectionHtml()
        : (Auth.isManager && list.length) ? allCertSectionHtml() : (list.length ? `
        <div style="margin-bottom: 24px;">
            <h3 style="font-size: 20px; font-weight: 800; letter-spacing: -0.3px;">${list.length} chứng nhận đã được cấp</h3>
        </div>
        
        <div class="vip-cert-list">${list.map(vipCertItemHtml).join('')}</div>`
        : emptyHtml(t('certificate.banChuaCoChungNhanNaoHoanThanh', 'Bạn chưa có chứng nhận nào. Hoàn thành một khóa học để được cấp chứng nhận.'), '<i class="ph ph-seal-check"></i>'));

    // Toàn bộ phần chứng nhận nội bộ ở trên giờ là thẻ đầu tiên; các thẻ còn lại (bên ngoài, duyệt, in danh
    // sách) tải lười khi được mở — xem openCertTab().
    content.innerHTML = certTabsHtml(internalHtml);

    // Một handler ủy quyền cho CẢ trang, thay vì gắn vào từng nút ngay sau lượt vẽ đầu tiên. Thẻ chứng
    // nhận của nhân viên cấp dưới chỉ ra đời khi người ta bấm vào một cái tên — gắn theo kiểu
    // querySelectorAll lúc dựng trang thì mọi nút sinh ra sau đó không có handler nào cả.
    content.addEventListener('click', (e) => {
        const openBtn = e.target.closest('[data-cert-open]');
        if (!openBtn) return;
        const certificate = certById.get(+openBtn.dataset.certOpen);
        if (certificate) showCertificate(certificate);
    });

    bindSubordinateSection();
    bindAllCertSection();
    bindExternalFeature();
    bindCertTabs();

    // ==================== CHỨNG NHẬN TOÀN CÔNG TY (Chuyên viên đào tạo...) ====================

    /** Khóa nhóm phòng ban của một chứng nhận — DTO chỉ mang tên phòng ban, không mang id. Người chưa gắn
     *  phòng ban dồn vào một nhóm riêng có khóa '-', không bao giờ trùng tên phòng ban thật. */
    function departmentKeyOf(c) { return c.departmentName || '-'; }

    function departmentLabelOf(key) {
        return key === '-' ? t('certificate.regNoDepartment', 'Chưa gắn phòng ban') : key;
    }

    /** Phòng ban theo vần, nhóm "Chưa gắn phòng ban" luôn nằm cuối — đó là chỗ dồn sót, không phải nơi xem đầu tiên. */
    function compareDepartmentKey(a, b) {
        if (a === b) return 0;
        if (a === '-') return 1;
        if (b === '-') return -1;
        return a.localeCompare(b, 'vi');
    }

    /** Gom một danh sách chứng nhận thành phòng ban -> người -> chứng nhận (mới cấp trước). */
    function groupCertificate(certificateList) {
        const departmentMap = new Map();
        certificateList.forEach(c => {
            const key = departmentKeyOf(c);
            if (!departmentMap.has(key)) departmentMap.set(key, new Map());
            const personMap = departmentMap.get(key);
            if (!personMap.has(c.userId)) personMap.set(c.userId, []);
            personMap.get(c.userId).push(c);
        });

        return [...departmentMap.entries()]
            .sort(([a], [b]) => compareDepartmentKey(a, b))
            .map(([key, personMap]) => ({
                key,
                personList: [...personMap.values()]
                    .map(certList => certList.sort((a, b) => new Date(b.issuedAt) - new Date(a.issuedAt)))
                    .sort((a, b) => a[0].userFullName.localeCompare(b[0].userFullName, 'vi'))
            }));
    }

    function allCertSectionHtml() {
        return `
            <div class="card ac-tools">
                <div class="card-body register-filter-row">
                    <div class="field">
                        <label for="ac-dept">${esc(t('certificate.department', 'Phòng ban'))}</label>
                        <select id="ac-dept">${allCertDepartmentOptionHtml()}</select>
                    </div>
                    <div class="field">
                        <label for="ac-user">${esc(t('certificate.employee', 'Nhân viên'))}</label>
                        <select id="ac-user">${allCertPersonOptionHtml()}</select>
                    </div>
                    <div class="field grow">
                        <label for="ac-keyword">${esc(t('certificate.regSearch', 'Tìm kiếm'))}</label>
                        <input type="search" id="ac-keyword" autocomplete="off"
                               placeholder="${esc(t('certificate.searchPh', 'Tìm tên, mã NV, chứng nhận...'))}">
                    </div>
                </div>
            </div>
            <div class="ac-summary" id="ac-summary"></div>
            <div id="ac-body"></div>`;
    }

    function allCertDepartmentOptionHtml() {
        const countMap = new Map();
        list.forEach(c => countMap.set(departmentKeyOf(c), (countMap.get(departmentKeyOf(c)) || 0) + 1));
        return `<option value="">${esc(t('certificate.allDepartment', 'Mọi phòng ban'))}</option>`
            + [...countMap.keys()].sort(compareDepartmentKey).map(key => `
                <option value="${esc(key)}">${esc(departmentLabelOf(key))} (${countMap.get(key)})</option>`).join('');
    }

    /** Ô chọn nhân viên chỉ liệt kê người của phòng ban đang chọn — chọn phòng ban trước là thu hẹp luôn danh sách tên. */
    function allCertPersonOptionHtml() {
        const personMap = new Map();
        list.filter(c => !allCertFilter.departmentKey || departmentKeyOf(c) === allCertFilter.departmentKey)
            .forEach(c => {
                const person = personMap.get(c.userId) || { ...c, count: 0 };
                person.count++;
                personMap.set(c.userId, person);
            });
        return `<option value="">${esc(t('certificate.allEmployee', 'Mọi nhân viên'))}</option>`
            + [...personMap.values()]
                .sort((a, b) => a.userFullName.localeCompare(b.userFullName, 'vi'))
                .map(p => `<option value="${p.userId}">${esc(p.userFullName)} · ${esc(p.employeeCode)} (${p.count})</option>`).join('');
    }

    function filteredAllCert() {
        const keyword = normalizeForSearch(allCertFilter.keyword.trim());
        return list.filter(c =>
            (!allCertFilter.departmentKey || departmentKeyOf(c) === allCertFilter.departmentKey) &&
            (!allCertFilter.userId || c.userId === +allCertFilter.userId) &&
            (!keyword || normalizeForSearch([c.userFullName, c.employeeCode, c.courseTitle, c.courseCode, c.certificateNo]
                .filter(Boolean).join(' ')).includes(keyword)));
    }

    function renderAllCert() {
        const body = document.getElementById('ac-body');
        if (!body) return;

        const shownList = filteredAllCert();
        const groupList = groupCertificate(shownList);
        const personCount = groupList.reduce((sum, g) => sum + g.personList.length, 0);
        const isFiltered = !!(allCertFilter.departmentKey || allCertFilter.userId || allCertFilter.keyword.trim());

        document.getElementById('ac-summary').innerHTML = `
            <h3>${shownList.length} ${esc(t('certificate.issuedCount', 'chứng nhận đã được cấp'))}</h3>
            <span class="muted">${personCount} ${esc(t('certificate.peopleUnit', 'nhân viên'))} · ${groupList.length} ${esc(t('certificate.departmentUnit', 'phòng ban'))}</span>
            ${isFiltered ? `<button type="button" class="btn ghost sm" data-ac-clear><i class="ph ph-x"></i> ${esc(t('certificate.clearFilter', 'Xóa bộ lọc'))}</button>` : ''}`;

        if (!shownList.length) {
            body.innerHTML = emptyHtml(t('certificate.filterEmpty', 'Không có chứng nhận nào khớp bộ lọc.'), '<i class="ph ph-funnel"></i>');
            return;
        }

        const certUnit = esc(t('certificate.certUnit', 'chứng nhận'));
        // Đang lọc thì xổ sẵn mọi nhóm còn lại — lọc xong mà vẫn phải bấm mở từng phòng thì bộ lọc mất nửa tác dụng.
        body.innerHTML = groupList.map(group => `
            <details class="ac-dept" data-dept-key="${esc(group.key)}"${isFiltered || openDepartmentKey.has(group.key) ? ' open' : ''}>
                <summary class="ac-dept-head">
                    <span class="ac-dept-icon"><i class="ph ph-buildings"></i></span>
                    <h3>${esc(departmentLabelOf(group.key))}</h3>
                    <span class="muted">${group.personList.length} ${esc(t('certificate.peopleUnit', 'nhân viên'))} · ${group.personList.reduce((sum, p) => sum + p.length, 0)} ${certUnit}</span>
                    <span class="ac-dept-faces" aria-hidden="true">
                        ${group.personList.slice(0, 5).map(certList => personAvatarHtml(personOfCert(certList[0]))).join('')}
                        ${group.personList.length > 5 ? `<span class="avatar ac-more">+${group.personList.length - 5}</span>` : ''}
                    </span>
                    <i class="ph ph-caret-down ac-dept-caret"></i>
                </summary>
                <div class="ac-dept-body">
                ${group.personList.map(certList => {
                    const c = certList[0];
                    return `
                        <div class="ac-person">
                            <div class="ac-person-head">
                                ${personAvatarHtml(personOfCert(c))}
                                <div class="ac-person-text">
                                    <b>${esc(c.userFullName)}</b>
                                    <small>${esc(c.employeeCode)}${c.positionName ? ' · ' + esc(c.positionName) : ''}</small>
                                </div>
                                <span class="badge blue">${certList.length} ${certUnit}</span>
                            </div>
                            <div class="vip-cert-list">${certList.map(vipCertItemHtml).join('')}</div>
                        </div>`;
                }).join('')}
                </div>
            </details>`).join('');
    }

    /** Thông tin người nhận theo đúng dạng personAvatarHtml() cần. */
    function personOfCert(c) {
        return { fullName: c.userFullName, avatarUrl: c.userAvatarUrl };
    }

    function bindAllCertSection() {
        const body = document.getElementById('ac-body');
        if (!body) return;

        const departmentSelect = document.getElementById('ac-dept');
        const personSelect = document.getElementById('ac-user');
        const keywordInput = document.getElementById('ac-keyword');
        enhanceSearchableSelect(departmentSelect, { placeholder: t('certificate.searchDept', 'Tìm phòng ban...') });
        enhanceSearchableSelect(personSelect, { placeholder: t('certificate.searchEmployee', 'Tìm tên hoặc mã NV...') });

        /** Dựng lại ô chọn nhân viên theo phòng ban; người đang chọn không còn thuộc phòng đó thì bỏ chọn. */
        const refreshPersonSelect = () => {
            personSelect.innerHTML = allCertPersonOptionHtml();
            personSelect.value = [...personSelect.options].some(o => o.value === allCertFilter.userId) ? allCertFilter.userId : '';
            allCertFilter.userId = personSelect.value;
            enhanceSearchableSelect(personSelect);
        };

        departmentSelect.addEventListener('change', () => {
            allCertFilter.departmentKey = departmentSelect.value;
            refreshPersonSelect();
            renderAllCert();
        });
        personSelect.addEventListener('change', () => {
            allCertFilter.userId = personSelect.value;
            renderAllCert();
        });
        let timer;
        keywordInput.addEventListener('input', () => {
            clearTimeout(timer);
            timer = setTimeout(() => { allCertFilter.keyword = keywordInput.value; renderAllCert(); }, 200);
        });
        // Ghi nhớ theo CÚ BẤM chứ không theo sự kiện 'toggle': nhóm tự xổ ra lúc đang lọc cũng phát 'toggle', và
        // ghi cả chúng vào thì xóa bộ lọc xong mọi phòng vẫn mở. Lúc click, .open vẫn còn là trạng thái CŨ.
        body.addEventListener('click', (e) => {
            const details = e.target.closest('.ac-dept > summary')?.parentElement;
            if (!details) return;
            if (details.open) openDepartmentKey.delete(details.dataset.deptKey);
            else openDepartmentKey.add(details.dataset.deptKey);
        });
        document.getElementById('ac-summary').addEventListener('click', (e) => {
            if (!e.target.closest('[data-ac-clear]')) return;
            Object.assign(allCertFilter, { departmentKey: '', userId: '', keyword: '' });
            departmentSelect.value = '';
            enhanceSearchableSelect(departmentSelect);
            keywordInput.value = '';
            refreshPersonSelect();
            renderAllCert();
        });

        renderAllCert();
    }

    // ==================== CHỨNG NHẬN CỦA NHÂN VIÊN CẤP DƯỚI ====================

    function subordinateSectionHtml() {
        if (!departmentList.length) return '';

        return `
            <div class="card sub-box" id="sub-cert-box">
                <div class="sub-head">
                    <div class="sub-head-icon"><i class="ph ph-users-three"></i></div>
                    <div class="sub-head-text">
                        <h3>Chứng nhận của nhân viên cấp dưới</h3>
                        <p id="sub-head-note">${esc(headNoteText())}</p>
                    </div>
                    ${singleDepartment ? '' : `
                        <div class="sub-dept">
                            <select id="dept-select" aria-label="Phòng ban">${departmentOptionListHtml()}</select>
                        </div>`}
                </div>
                <div class="ops-kpi sub-kpi" id="sub-kpi">${kpiHtml()}</div>
                <div class="sub-layout">
                    <aside class="sub-people" aria-label="Danh sách nhân viên">
                        <div class="sub-people-tools">
                            <div class="sub-search">
                                <i class="ph ph-magnifying-glass"></i>
                                <input id="sub-search" type="search" autocomplete="off" aria-label="Tìm nhân viên"
                                       placeholder="Tìm theo tên hoặc mã nhân viên...">
                            </div>
                            <div class="sub-seg" id="sub-seg" role="group" aria-label="Lọc theo chứng nhận">
                                <button type="button" data-filter="all">Tất cả</button>
                                <button type="button" data-filter="has" title="Đã có chứng nhận">Đã có</button>
                                <button type="button" data-filter="none" title="Chưa có chứng nhận">Chưa có</button>
                            </div>
                        </div>
                        <div class="sub-people-list" id="sub-people-list"></div>
                    </aside>
                    <section class="sub-panel" id="sub-cert-panel" aria-live="polite">${panelHintHtml()}</section>
                </div>
            </div>`;
    }

    /** Dòng phụ dưới tiêu đề khối: đang xem phòng ban nào, bao nhiêu người. */
    function headNoteText() {
        const department = departmentList.find(d => d.departmentId === selectedDepartmentId);
        if (department) return `${department.name} · ${department.employeeCount} nhân viên`;
        const total = allDepartmentEmployeeCount();
        if (selectedDepartmentId === ALL_DEPARTMENT) return `Tất cả ${departmentList.length} phòng ban · ${total} nhân viên`;
        return `${departmentList.length} phòng ban · ${total} nhân viên — chọn một phòng ban để bắt đầu`;
    }

    function allDepartmentEmployeeCount() {
        return departmentList.reduce((sum, d) => sum + d.employeeCount, 0);
    }

    /** Tham số lọc phòng ban gửi lên máy chủ — "Tất cả phòng ban" thì không gửi gì. */
    function departmentQuery(departmentId) {
        return departmentId === ALL_DEPARTMENT ? '' : `departmentId=${departmentId}`;
    }

    /** Dải ba con số của phòng ban đang chọn. Chưa chọn phòng ban thì hiện gạch ngang thay vì số 0 — số 0 nói
     *  "phòng này không ai có chứng nhận", điều không hề đúng. */
    function kpiHtml() {
        const ready = singleDepartment || selectedDepartmentId !== null;
        const department = selectedDepartmentId === ALL_DEPARTMENT
            ? { employeeCount: allDepartmentEmployeeCount() }
            : departmentList.find(d => d.departmentId === selectedDepartmentId);
        const total = deptPeopleList.length;
        const withCert = deptPeopleList.filter(u => u.certificateCount > 0).length;
        const percent = total ? Math.round(withCert / total * 100) : 0;
        // Thẻ giữa đếm CHỨNG NHẬN (một người có thể có nhiều cái), số người có chứng nhận xuống dòng dưới.
        const certTotal = deptPeopleList.reduce((sum, u) => sum + u.certificateCount, 0);

        const cell = (i, tone, icon, label, value, foot) => `
            <div class="ops-kpi-cell" style="--i:${i}; --tone:${tone}">
                <div class="ops-kpi-top"><span class="ops-kpi-label">${label}</span><span class="ops-kpi-icon"><i class="ph ${icon}"></i></span></div>
                <div class="ops-kpi-value">${ready ? value : '—'}</div>
                <div class="ops-kpi-foot">${ready ? foot : '&nbsp;'}</div>
            </div>`;

        return cell(0, 'var(--primary)', 'ph-users', 'Nhân viên', total,
                // Máy chủ trả tối đa 100 người một lượt — nói rõ khi phòng đông hơn thế, kẻo người xem tưởng thiếu người.
                department && department.employeeCount > total ? `Hiện ${total}/${department.employeeCount} người — gõ tên để tìm`
                    : selectedDepartmentId === ALL_DEPARTMENT ? 'Toàn công ty' : 'Trong phòng ban')
            + cell(1, 'var(--success)', 'ph-seal-check', 'Chứng nhận nội bộ', certTotal,
                `Của ${withCert} nhân viên (${percent}%)`)
            + cell(2, 'var(--warning)', 'ph-hourglass-medium', 'Nhân viên chưa có chứng nhận', total - withCert, 'Chưa hoàn thành khóa nào');
    }

    function refreshSummary() {
        const note = document.getElementById('sub-head-note');
        const kpi = document.getElementById('sub-kpi');
        if (note) note.textContent = headNoteText();
        if (kpi) kpi.innerHTML = kpiHtml();
    }

    /** Ảnh đại diện, hoặc chữ cái đầu của tên khi chưa có ảnh. */
    function personAvatarHtml(person) {
        return person?.avatarUrl
            ? `<img class="avatar" src="${esc(person.avatarUrl)}" alt="">`
            : `<span class="avatar" aria-hidden="true">${esc(initial(person?.fullName))}</span>`;
    }

    function emptyBlockHtml(icon, title, text) {
        return `
            <div class="sub-empty">
                <div class="sub-empty-icon"><i class="ph ${icon}"></i></div>
                <h4>${esc(title)}</h4>
                <p>${esc(text)}</p>
            </div>`;
    }

    function departmentOptionListHtml() {
        return '<option value="">— Chọn phòng ban —</option>'
            + `<option value="${ALL_DEPARTMENT}">Tất cả phòng ban (${allDepartmentEmployeeCount()} người)</option>`
            + departmentList.map(d => `
                <option value="${d.departmentId}">${esc(d.name)} (${d.employeeCount} người)</option>`).join('');
    }

    /** Hàm KHAI BÁO chứ không phải const-mũi-tên: subordinateSectionHtml() gọi nó ngay lúc dựng trang, mà
     *  chỗ dựng đó chạy TRƯỚC dòng này. Một const ở đây còn nằm trong vùng chết tạm thời tại thời điểm ấy,
     *  và cả trang vỡ bằng ReferenceError với bất kỳ ai có cấp dưới. */
    /** Một thẻ chứng nhận kiểu "vàng kim" — mẫu dùng CHUNG cho danh sách chứng nhận của chính mình và cho
     *  khung xổ ra của nhân viên cấp dưới. Hai chỗ cùng vẽ một thứ thì phải cùng một hàm: chép ra hai bản
     *  là lần sau sửa thẻ ở chỗ này mà quên chỗ kia, rồi hai khối trên cùng một trang lại trông khác nhau.
     *
     *  Hàm KHAI BÁO chứ không phải const — nó được gọi ngay lúc dựng trang, ở đoạn chạy TRƯỚC dòng này. */
    function vipCertItemHtml(c) {
        return `
            <div class="cert-card interactive vip-cert-item ${c.isRevoked ? 'revoked' : ''}">
                <span class="cert-card-shine"></span>
                <i class="ph-fill ph-seal-check cert-card-stamp"></i>

                <div class="vip-cert-main">
                    <div class="cert-card-no">
                        <i class="ph-fill ph-medal"></i> ${esc(c.certificateNo)}
                    </div>
                    <div class="vip-cert-user">
                        <b>${esc(c.userFullName)}</b>
                        <span>${esc(c.employeeCode)}${c.departmentName ? ' · ' + esc(c.departmentName) : ''}</span>
                    </div>
                    <div class="cert-card-course vip-cert-course">
                        ${esc(c.courseTitle)}
                        <span class="small muted" style="font-family: sans-serif; font-weight: 500;">${esc(c.courseCode)}</span>
                    </div>
                </div>

                <div class="vip-cert-meta">
                    <div class="vip-cert-score"><span class="muted">Đánh giá:</span> <b>${c.finalScore}</b><small class="muted">/100</small></div>
                    <div><span class="muted">Cấp ngày:</span> <b>${formatDate(c.issuedAt)}</b></div>
                    <div>
                        ${c.isRevoked
                            ? '<span data-i18n="account.daThuHoi" class="badge red">Đã thu hồi</span>'
                            : (c.expiredAt && new Date(c.expiredAt) < new Date())
                                ? '<span data-i18n="certificate.hetHan" class="badge orange">Hết hạn</span>'
                                : `<span data-i18n="certificate.conHieuLuc" class="badge green">Còn hiệu lực</span> <span class="small muted">đến ${formatDate(c.expiredAt)}</span>`}
                    </div>
                </div>

                <div class="vip-cert-actions">
                    <button type="button" class="btn ghost sm" data-cert-open="${c.id}">Xem chi tiết</button>
                    <a data-i18n="certificate.xacThuc" class="btn ghost sm" href="/pages/certificate/verify.html?code=${encodeURIComponent(c.certificateNo)}" target="_blank">Xác thực</a>
                </div>
            </div>`;
    }

    function panelHintHtml() {
        return (!singleDepartment && selectedDepartmentId === null)
            ? emptyBlockHtml('ph-buildings', 'Chọn phòng ban để bắt đầu',
                'Chọn phòng ban ở góc trên, rồi bấm vào một nhân viên trong danh sách để xem chứng nhận của người đó.')
            : emptyBlockHtml('ph-hand-pointing', 'Chọn một nhân viên',
                'Bấm vào một người trong danh sách nhân viên để xem các chứng nhận họ đã được cấp.');
    }

    /** Vẽ lại danh sách nhân viên theo trạng thái hiện tại (phòng ban, từ khóa, bộ lọc nhanh).
     *  @param {boolean} [loading] đang tải danh sách mới — hiện khung xương thay cho tên. */
    function renderPeopleList(loading = false) {
        const box = document.getElementById('sub-people-list');
        if (!box) return;

        const needDepartment = !singleDepartment && selectedDepartmentId === null;
        const searchInput = document.getElementById('sub-search');
        searchInput.disabled = needDepartment;
        document.querySelectorAll('#sub-seg [data-filter]').forEach(button => {
            button.disabled = needDepartment;
            button.classList.toggle('on', button.dataset.filter === peopleFilter);
            button.setAttribute('aria-pressed', String(button.dataset.filter === peopleFilter));
        });

        const noteHtml = (icon, text) => `<div class="sub-list-note"><i class="ph ${icon}"></i>${esc(text)}</div>`;
        if (loading) { box.innerHTML = skeletonHtml('list', { count: 5 }); return; }
        if (needDepartment) { box.innerHTML = noteHtml('ph-buildings', 'Chọn phòng ban để xem danh sách nhân viên.'); return; }

        const shownList = subordinateList.filter(u =>
            peopleFilter === 'all' || (peopleFilter === 'has') === (u.certificateCount > 0));

        if (!shownList.length) {
            box.innerHTML = noteHtml('ph-user-circle-dashed',
                searchInput.value.trim() ? 'Không có nhân viên nào khớp từ khóa.'
                : peopleFilter === 'has' ? 'Chưa ai trong phòng có chứng nhận.'
                : peopleFilter === 'none' ? 'Mọi người trong phòng đều đã có chứng nhận.'
                : 'Phòng ban này chưa có nhân viên.');
            return;
        }

        const personHtml = u => `
            <button type="button" class="sub-person${u.userId === selectedUserId ? ' on' : ''}" data-user="${u.userId}"
                    aria-pressed="${u.userId === selectedUserId}">
                ${personAvatarHtml(u)}
                <span class="sub-person-text">
                    <b>${esc(u.fullName)}</b>
                    <small>${esc(u.employeeCode)}${u.positionName ? ' · ' + esc(u.positionName) : ''}</small>
                </span>
                <span class="sub-count${u.certificateCount ? ' has' : ''}" title="${u.certificateCount} chứng nhận">
                    <i class="ph ph-seal-check"></i>${u.certificateCount}
                </span>
            </button>`;

        if (selectedDepartmentId !== ALL_DEPARTMENT) { box.innerHTML = shownList.map(personHtml).join(''); return; }

        // "Tất cả phòng ban": gom theo phòng, theo đúng thứ tự của ô chọn phòng ban (máy chủ đã xếp theo vần).
        const noDepartment = 'Chưa gắn phòng ban';
        const groupMap = new Map(departmentList.map(d => [d.departmentId === 0 ? noDepartment : d.name, []]));
        shownList.forEach(u => {
            const key = u.departmentName || noDepartment;
            if (!groupMap.has(key)) groupMap.set(key, []);
            groupMap.get(key).push(u);
        });
        // Nhóm "Chưa gắn phòng ban" luôn xuống cuối — đó là chỗ dồn sót, không phải nơi xem đầu tiên.
        box.innerHTML = [...groupMap.entries()]
            .filter(([, people]) => people.length)
            .sort(([a], [b]) => (a === noDepartment) - (b === noDepartment))
            .map(([name, people]) => `
            <div class="sub-group-label"><i class="ph ph-buildings"></i><span>${esc(name)}</span><small>${people.length}</small></div>
            ${people.map(personHtml).join('')}`).join('');
    }

    /** Một chỗ gắn sự kiện cho cả khối, thay vì gắn lại cho từng dòng sau mỗi lượt tìm kiếm: danh sách được
     *  vẽ lại liên tục, mà handler gắn vào dòng cũ thì mất theo dòng đó. */
    function bindSubordinateSection() {
        const box = document.getElementById('sub-cert-box');
        if (!box) return;

        // CHỈ lo việc chọn tên. Nút "Xem chi tiết" trên từng thẻ do handler ủy quyền ở cấp #content lo —
        // khối này nằm TRONG #content nên bắt thêm ở đây là hộp thoại chứng nhận mở hai lần một lúc.
        const departmentSelect = document.getElementById('dept-select');
        if (departmentSelect) {
            enhanceSearchableSelect(departmentSelect, { placeholder: 'Tìm phòng ban...' });
            departmentSelect.addEventListener('change', () => {
                if (departmentSelect.value) selectDepartment(+departmentSelect.value);
            });
            // Nhiều phòng ban (Admin / Tổng Giám Đốc) thì mở sẵn "Tất cả phòng ban": người nhìn toàn công ty cần
            // thấy ngay mọi nhân viên, không phải lần lượt mở từng phòng mới biết ai ở đâu. Vẫn tải SAU lượt vẽ
            // đầu, nên trang không phải chờ danh sách tên mới hiện ra.
            departmentSelect.value = String(ALL_DEPARTMENT);
            enhanceSearchableSelect(departmentSelect);
            selectDepartment(ALL_DEPARTMENT);
        }

        document.getElementById('sub-people-list').addEventListener('click', (e) => {
            const item = e.target.closest('[data-user]');
            if (item) selectSubordinate(+item.dataset.user);
        });
        document.getElementById('sub-seg').addEventListener('click', (e) => {
            const button = e.target.closest('[data-filter]');
            if (!button || button.disabled) return;
            peopleFilter = button.dataset.filter;
            renderPeopleList();
        });
        renderPeopleList();

        let timer;
        document.getElementById('sub-search').addEventListener('input', (e) => {
            clearTimeout(timer);
            const keyword = e.target.value.trim();
            // Gõ xong 300ms mới gọi API, không phải mỗi phím một request.
            timer = setTimeout(async () => {
                // Tìm kiếm chạy TRONG phòng ban đang chọn. Bỏ qua điều đó thì gõ một cái tên phổ biến là
                // kết quả nhảy ra người của phòng khác, mâu thuẫn với cái tab phòng ban đang sáng ngay trên.
                if (!singleDepartment && selectedDepartmentId === null) return;
                try {
                    subordinateList = await API.getSilent(
                        `/certificate/subordinate?${departmentQuery(selectedDepartmentId)}`
                        + `&keyword=${encodeURIComponent(keyword)}`);
                } catch { return; }
                subordinateList.forEach(u => personById.set(u.userId, u));
                renderPeopleList();
                // CỐ Ý giữ nguyên khung kết quả bên dưới: người đang xem chứng nhận của một nhân viên rồi
                // mới gõ tìm người tiếp theo thì xóa trắng nó đi là cướp mất thứ họ đang đọc dở. Dòng tên
                // ở đầu khung vẫn cho biết đó là chứng nhận của ai.
            }, 300);
        });
    }

    async function selectDepartment(departmentId) {
        if (departmentId === selectedDepartmentId) return;

        selectedDepartmentId = departmentId;
        selectedUserId = null;
        peopleFilter = 'all';
        deptPeopleList = [];

        // Đổi phòng ban thì người đang xem dở không còn thuộc phạm vi đang lọc nữa — trả khung kết quả về
        // lời gợi ý, đừng để chứng nhận của một người phòng khác nằm dưới một phòng ban vừa đổi.
        const panel = document.getElementById('sub-cert-panel');
        panel.innerHTML = panelHintHtml();
        document.getElementById('sub-search').value = '';

        refreshSummary();
        renderPeopleList(true);

        try {
            subordinateList = await API.getSilent(`/certificate/subordinate?${departmentQuery(departmentId)}`);
        } catch (ex) {
            if (selectedDepartmentId === departmentId)
                document.getElementById('sub-people-list').innerHTML =
                    `<div class="sub-list-note"><i class="ph ph-warning"></i>${esc(ex.message)}</div>`;
            return;
        }

        // Chọn nhanh sang phòng ban khác trong lúc request còn chạy thì kết quả về muộn này đã lỗi thời.
        if (selectedDepartmentId !== departmentId) return;

        subordinateList.forEach(u => personById.set(u.userId, u));
        deptPeopleList = subordinateList;
        refreshSummary();
        renderPeopleList();
    }

    async function selectSubordinate(userId) {
        selectedUserId = userId;

        // Đổi dòng đang sáng tại chỗ thay vì vẽ lại cả danh sách — giữ nguyên vị trí cuộn của cột trái.
        document.querySelectorAll('#sub-people-list [data-user]').forEach(item => {
            const on = +item.dataset.user === userId;
            item.classList.toggle('on', on);
            item.setAttribute('aria-pressed', String(on));
        });

        const panel = document.getElementById('sub-cert-panel');
        const person = personById.get(userId);
        // Màn hẹp: danh sách và khung kết quả xếp chồng — đưa người xem xuống chỗ chứng nhận vừa mở.
        if (matchMedia('(max-width: 900px)').matches) panel.scrollIntoView({ behavior: 'smooth', block: 'start' });

        if (!certByUser.has(userId)) {
            panel.innerHTML = skeletonHtml('list', { count: 2, avatar: false });
            let certificateList;
            try {
                certificateList = await API.getSilent(`/certificate/by-user/${userId}`);
            } catch (ex) {
                if (selectedUserId === userId)
                    panel.innerHTML = emptyBlockHtml('ph-warning', 'Không tải được chứng nhận', ex.message);
                return;
            }

            // Bấm nhanh sang người khác trong lúc request còn đang chạy thì kết quả về muộn này đã lỗi thời
            // — ghi nó vào bộ nhớ đệm thì được, nhưng KHÔNG được vẽ đè lên người đang chọn.
            certByUser.set(userId, certificateList);
            certificateList.forEach(c => certById.set(c.id, c));
            if (selectedUserId !== userId) return;
        }

        panel.innerHTML = subordinatePanelHtml(person, certByUser.get(userId));
    }

    function subordinatePanelHtml(person, certificateList) {
        const now = new Date();
        const activeCount = certificateList.filter(c =>
            !c.isRevoked && !(c.expiredAt && new Date(c.expiredAt) < now)).length;

        // Đầu khung luôn nói rõ ĐANG xem chứng nhận của ai — kèm mã nhân viên và chức danh để phân biệt hai
        // người trùng tên.
        const head = person ? `
            <div class="sub-profile">
                ${personAvatarHtml(person)}
                <div class="sub-profile-text">
                    <h4>${esc(person.fullName)}</h4>
                    <p>${esc(person.employeeCode)}${person.departmentName ? ' · ' + esc(person.departmentName) : ''}${person.positionName ? ' · ' + esc(person.positionName) : ''}</p>
                </div>
                <div class="sub-profile-chips">
                    <span class="badge blue">${certificateList.length} chứng nhận</span>
                    ${certificateList.length ? `<span class="badge green">${activeCount} còn hiệu lực</span>` : ''}
                </div>
            </div>` : '';

        if (!certificateList.length)
            return head + emptyBlockHtml('ph-seal-question', 'Chưa có chứng nhận',
                'Người này chưa hoàn thành khóa học nào được cấp chứng nhận.');

        return head + `<div class="vip-cert-list">${certificateList.map(vipCertItemHtml).join('')}</div>`;
    }

    function showCertificate(c) {
        // Tên dài thì thu nhỏ chữ thư pháp để vẫn gọn trong cột trái (mỗi ký tự rộng khoảng 0,48 lần cỡ chữ).
        const nameLength = (c.userFullName || '').length;
        const nameSize = nameLength <= 16 ? 7.4 : nameLength <= 20 ? 6 : nameLength <= 26 ? 4.8 : 4;
        // Số vùng (1-4) ở mép trên và dưới, chữ vùng (A-C) ở mép trái và phải, như khung của bản vẽ kỹ thuật.
        const zone = (items) => items.map(x => `<span>${x}</span>`).join('');
        document.getElementById('cert-view').innerHTML = `
            <div class="hbc-stage">
                <div class="hbc ${c.isRevoked ? 'revoked' : ''}">
                    <div class="hbc-shell">
                        <div class="hbc-paper">
                            <div class="hbc-frame"></div>
                            <div class="hbc-zones top">${zone([1, 2, 3, 4])}</div><div class="hbc-zones bottom">${zone([1, 2, 3, 4])}</div>
                            <div class="hbc-zones left">${zone(['A', 'B', 'C'])}</div><div class="hbc-zones right">${zone(['A', 'B', 'C'])}</div>

                            <div class="hbc-sheet">
                                <div class="hbc-copy">
                                    <div class="hbc-brand" data-r style="--i:0">
                                        <img src="/img/HB.png" alt="">
                                        <span><b>HB E-Learning</b><small>Đào tạo nội bộ Hòa Bình</small></span>
                                    </div>
                                    <h1 class="hbc-title" data-r style="--i:1">Chứng nhận<br><em>hoàn thành</em></h1>
                                    <p class="hbc-lead" data-r style="--i:2">Trân trọng trao tặng</p>
                                    <div class="hbc-name" data-r style="--i:3;--nf:${nameSize}cqw">${esc(c.userFullName)}</div>
                                    <p class="hbc-text" data-r style="--i:4">đã hoàn thành xuất sắc khóa đào tạo</p>
                                    <div class="hbc-course" data-r style="--i:5">${esc(c.courseTitle)}</div>
                                </div>

                                <div class="hbc-detail" data-r style="--i:3">
                                    <div class="hbc-disc"></div>
                                    <div class="hbc-mascot-wrap"><img class="hbc-mascot" src="/emoji/hb-mascot-lg.png" alt=""></div>
                                </div>

                                <div class="hbc-titleblock" data-r style="--i:6">
                                    <div class="hbc-cell qr">
                                        ${c.qrCodeUrl ? `<img class="hbc-qr" src="${esc(c.qrCodeUrl)}" alt="QR xác thực">` : '<div class="hbc-qr-empty"></div>'}
                                        <span class="hbc-v">Quét mã để<br>xác thực</span>
                                    </div>
                                    <div class="hbc-cell"><div class="hbc-k">Mã nhân viên</div><div class="hbc-v">${esc(c.employeeCode)}</div></div>
                                    <div class="hbc-cell"><div class="hbc-k">Ngày cấp</div><div class="hbc-v">${formatDate(c.issuedAt)}</div></div>
                                    <div class="hbc-cell"><div class="hbc-k">Số hiệu</div><div class="hbc-v">${esc(c.certificateNo)}</div></div>
                                    <div class="hbc-cell"><div class="hbc-k"><span class="mark"></span>Đơn vị cấp</div><div class="hbc-v hbc-sign-name">HB Group</div></div>
                                </div>
                            </div>

                            <div class="hbc-sheen"></div>
                            ${c.isRevoked ? '<div class="hbc-revoked">ĐÃ BỊ THU HỒI</div>' : ''}
                        </div>
                    </div>
                </div>
            </div>`;
        bindCertificateTilt();
        openModal('modal-cert');
    }

    /** Nghiêng 3D + vệt sáng + thị sai của linh vật, chạy theo con trỏ. Chỉ khi có chuột (hover) và người dùng không tắt
     *  hiệu ứng; các listener gắn lên chính phần tử vừa vẽ nên bị dọn cùng nó ở lần vẽ sau. */
    function bindCertificateTilt() {
        const stage = document.querySelector('#cert-view .hbc-stage');
        const cert = stage?.querySelector('.hbc');
        const sheen = stage?.querySelector('.hbc-sheen');
        if (!stage || !cert) return;
        if (matchMedia('(prefers-reduced-motion: reduce)').matches || !matchMedia('(hover: hover)').matches) return;

        const MAX_TILT = 6;
        let frame = 0;
        stage.addEventListener('pointermove', e => {
            if (frame) return;
            frame = requestAnimationFrame(() => {
                frame = 0;
                const box = stage.getBoundingClientRect();
                const nx = Math.max(-1, Math.min(1, (e.clientX - box.left - box.width / 2) / (box.width / 2)));
                const ny = Math.max(-1, Math.min(1, (e.clientY - box.top - box.height / 2) / (box.height / 2)));
                cert.style.setProperty('--ry', `${(nx * MAX_TILT).toFixed(2)}deg`);
                cert.style.setProperty('--rx', `${(-ny * MAX_TILT).toFixed(2)}deg`);
                cert.style.setProperty('--px', nx.toFixed(3));
                cert.style.setProperty('--py', ny.toFixed(3));
                stage.style.setProperty('--mx', `${e.clientX - box.left}px`);
                stage.style.setProperty('--my', `${e.clientY - box.top}px`);
                // offsetLeft/offsetWidth là kích thước bố cục, không đổi theo phép nghiêng nên vệt sáng không giật.
                const along = (e.clientX - box.left - cert.offsetLeft) / cert.offsetWidth * 100;
                sheen.style.setProperty('--sx', (along - 27).toFixed(1));
            });
        });
        stage.addEventListener('pointerenter', () => stage.classList.add('is-live'));
        stage.addEventListener('pointerleave', () => {
            stage.classList.remove('is-live');
            ['--rx', '--ry'].forEach(name => cert.style.setProperty(name, '0deg'));
            ['--px', '--py'].forEach(name => cert.style.setProperty(name, '0'));
        });
    }

    // ==================== THẺ TRANG ====================
    // Nội bộ (mọi người) | Bên ngoài (nhân viên tự gửi) | Duyệt + In danh sách (chỉ Admin). Admin chỉ quản lý,
    // không tự gửi chứng nhận bên ngoài — nên không có thẻ "Bên ngoài" (máy chủ cũng chặn). Thẻ đang mở ghi lên
    // URL (?tab=...) để tải lại trang, hoặc bấm thông báo, là về đúng thẻ đó.

    function certTabsHtml(internalHtml) {
        const tabList = [
            ['internal', 'ph-seal-check', t('certificate.tabInternal', 'Chứng nhận nội bộ')],
            ...(hasMineTab ? [['mine', 'ph-medal', t('certificate.tabMine', 'Chứng nhận của tôi')]] : []),
            ...(Auth.isAdmin ? [
                ['review', 'ph-scan', t('certificate.tabExternal', 'Chứng nhận bên ngoài')],
                ['register', 'ph-printer', t('certificate.tabRegister', 'In danh sách')],
            ] : [
                ['external', 'ph-scan', t('certificate.tabExternal', 'Chứng nhận bên ngoài')],
                ...(isHrReviewer ? [['review', 'ph-stamp', t('certificate.tabHrReview', 'Nhân sự xác nhận')]] : []),
                ...(hasRegisterTab ? [['register', 'ph-printer', t('certificate.tabRegister', 'In danh sách')]] : []),
            ]),
        ];
        return `
            <div class="tabs cert-tabs" role="tablist">
                ${tabList.map(([key, icon, label]) => `
                    <button type="button" role="tab" data-tab="${key}">
                        <i class="ph ${icon}"></i> ${esc(label)}${key === 'review'
                            ? ' <span class="tab-count hidden" id="review-count"></span>' : ''}</button>`).join('')}
            </div>
            <div class="tab-panel" id="tab-internal">${internalHtml}</div>
            ${hasMineTab ? '<div class="tab-panel" id="tab-mine"></div>' : ''}
            ${Auth.isAdmin
                ? '<div class="tab-panel" id="tab-review"></div><div class="tab-panel" id="tab-register"></div>'
                : '<div class="tab-panel" id="tab-external"></div>'
                    + (isHrReviewer ? '<div class="tab-panel" id="tab-review"></div>' : '')
                    + (hasRegisterTab ? '<div class="tab-panel" id="tab-register"></div>' : '')}`;
    }

    function bindCertTabs() {
        content.querySelectorAll('.cert-tabs [data-tab]').forEach(button =>
            button.addEventListener('click', () => openCertTab(button.dataset.tab)));
        const fromUrl = qs('tab');
        openCertTab(content.querySelector(`.cert-tabs [data-tab="${fromUrl}"]`) ? fromUrl : 'internal');
        if (Auth.isAdmin) refreshReviewCount();
    }

    function openCertTab(key) {
        content.querySelectorAll('.cert-tabs [data-tab]').forEach(button => {
            button.classList.toggle('active', button.dataset.tab === key);
            button.setAttribute('aria-selected', String(button.dataset.tab === key));
        });
        content.querySelectorAll(':scope > .tab-panel').forEach(panel =>
            panel.classList.toggle('active', panel.id === `tab-${key}`));

        const url = new URL(location.href);
        if (key === 'internal') url.searchParams.delete('tab'); else url.searchParams.set('tab', key);
        history.replaceState(null, '', url);

        if (key === 'mine' && !loadedTab.mine) loadMineTab();
        if (key === 'external' && !loadedTab.external) loadExternalTab();
        if (key === 'review' && !loadedTab.review) loadReviewTab();
        if (key === 'register' && !loadedTab.register) loadRegisterTab();
    }

    /** Con số trên thẻ chứng nhận bên ngoài của Admin: bao nhiêu giấy AI chưa đối chiếu xong — tăng mãi là dấu
     *  hiệu máy chủ AI đang tắt. */
    async function refreshReviewCount() {
        const badge = document.getElementById('review-count');
        if (!badge || !Auth.isAdmin) return;
        try {
            const { count } = await API.getSilent('/externalcertificate/pending-count');
            badge.textContent = count > 99 ? '99+' : count;
            badge.title = t('certificate.extPendingCount', 'Giấy AI chưa đối chiếu xong');
            badge.classList.toggle('hidden', !count);
        } catch { /* chỉ là con số trang trí — lỗi thì thôi */ }
    }

    // ==================== CHỨNG NHẬN NỘI BỘ CỦA TÔI ====================

    async function loadMineTab() {
        loadedTab.mine = true;
        const panel = document.getElementById('tab-mine');
        panel.innerHTML = skeletonHtml('cards', { count: 3, thumb: false });
        try {
            myCertList = await API.get('/certificate/my');
        } catch (ex) {
            loadedTab.mine = false;
            panel.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
            return;
        }
        // Nút "Xem chi tiết" tra theo id qua handler ủy quyền chung của trang.
        myCertList.forEach(c => certById.set(c.id, c));
        panel.innerHTML = myCertList.length ? `
            <div class="ac-summary">
                <h3>${myCertList.length} ${esc(t('certificate.issuedCount', 'chứng nhận đã được cấp'))}</h3>
            </div>
            <div class="vip-cert-list">${myCertList.map(vipCertItemHtml).join('')}</div>`
            : emptyHtml(t('certificate.banChuaCoChungNhanNaoHoanThanh', 'Bạn chưa có chứng nhận nào. Hoàn thành một khóa học để được cấp chứng nhận.'), '<i class="ph ph-seal-check"></i>');
    }

    // ==================== CHỨNG NHẬN BÊN NGOÀI CỦA TÔI ====================

    async function loadExternalTab() {
        loadedTab.external = true;
        const panel = document.getElementById('tab-external');
        panel.innerHTML = skeletonHtml('list', { count: 3, avatar: false });
        try {
            myExternalList = await API.get('/externalcertificate/my');
        } catch (ex) {
            loadedTab.external = false;
            panel.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
            return;
        }
        myExternalList.forEach(c => externalById.set(c.id, c));
        renderExternalTab();
    }

    function renderExternalTab() {
        const panel = document.getElementById('tab-external');
        panel.innerHTML = `
            <div class="ext-hero">
                <div>
                    <h3>${esc(t('certificate.extHeroTitle', 'Chứng nhận bên ngoài'))}</h3>
                    <p>${esc(t('certificate.extHeroText', 'Đã học ở trường, trung tâm hay hãng bên ngoài? Chụp giấy chứng nhận bằng camera hoặc tải bản PDF lên — AI đọc giấy, tự điền thông tin và đối chiếu với tài khoản của bạn. Khớp là chứng nhận vào hồ sơ năng lực ngay, không phải chờ duyệt.'))}</p>
                </div>
                <button type="button" class="btn" data-ext-add><i class="ph ph-scan"></i> ${esc(t('certificate.extAdd', 'Thêm chứng nhận'))}</button>
            </div>
            ${myExternalList.length
                ? `<div class="ext-list">${myExternalList.map(extCardHtml).join('')}</div>`
                : emptyHtml(t('certificate.extEmpty', 'Bạn chưa gửi chứng nhận bên ngoài nào.'), '<i class="ph ph-certificate"></i>')}`;
        scheduleMyRefresh();
    }

    /** Còn giấy AI đang đối chiếu nền thì 15 giây hỏi lại một lần — kết quả tự hiện ra, khỏi bấm tải lại trang. */
    function scheduleMyRefresh() {
        clearTimeout(myRefreshTimer);
        if (!myExternalList.some(c => c.status === 'Pending')) return;
        myRefreshTimer = setTimeout(async () => {
            const panel = document.getElementById('tab-external');
            if (panel?.classList.contains('active') && !document.hidden) {
                try {
                    const fresh = await API.getSilent('/externalcertificate/my');
                    const signature = (listValue) => listValue.map(c => `${c.id}:${c.status}`).join(',');
                    const isChanged = signature(fresh) !== signature(myExternalList);
                    myExternalList = fresh;
                    fresh.forEach(c => externalById.set(c.id, c));
                    if (isChanged) { renderExternalTab(); return; }
                } catch { /* mạng chập chờn — lượt sau hỏi lại */ }
            }
            scheduleMyRefresh();
        }, 15000);
    }

    function extCardHtml(c) {
        const isPdf = c.contentType === 'application/pdf';
        return `
            <article class="ext-card status-${c.status.toLowerCase()}">
                <div class="ext-card-icon"><i class="ph-duotone ${isPdf ? 'ph-file-pdf' : 'ph-image'}"></i></div>
                <div>
                    <div class="ext-card-top"><h4>${esc(c.title)}</h4>${extStatusBadge(c)}</div>
                    <div class="ext-card-meta">
                        <span><i class="ph ph-buildings"></i> ${esc(c.issuer)}</span>
                        ${c.certificateNo ? `<span><i class="ph ph-hash"></i> ${esc(c.certificateNo)}</span>` : ''}
                        ${c.hrConfirmedAt ? `<span><i class="ph ph-stamp"></i> ${esc(c.hrConfirmedByName || '—')}</span>` : ''}
                        <span><i class="ph ph-calendar-blank"></i> ${esc(t('certificate.extIssuedOn', 'Cấp'))} ${formatDate(c.issuedAt)}${c.expiredAt
                            ? ` · ${esc(t('certificate.extExpiresOn', 'hết hạn'))} ${formatDate(c.expiredAt)}` : ''}</span>
                    </div>
                    ${c.status === 'Rejected' && c.rejectReason
                        ? `<div class="ext-card-reason"><i class="ph-fill ph-warning-circle"></i><span>${esc(c.rejectReason)}</span></div>` : ''}
                    ${c.status === 'Pending'
                        ? `<div class="ext-card-reason" style="color:var(--text-muted)"><span class="spinner-inline"></span><span>${esc(t('certificate.extAiWorking', 'AI đang đọc giấy và đối chiếu — có kết quả sẽ báo cho bạn.'))}</span></div>` : ''}
                </div>
                <div class="ext-card-actions">
                    <button type="button" class="btn ghost sm" data-ext-view="${c.id}"><i class="ph ph-eye"></i> ${esc(t('certificate.extView', 'Xem'))}</button>
                    ${c.status !== 'Approved'
                        ? `<button type="button" class="btn ghost sm" data-ext-delete="${c.id}"><i class="ph ph-trash"></i> ${esc(t('common.delete', 'Xóa'))}</button>` : ''}
                </div>
            </article>`;
    }

    /** Huy hiệu trạng thái — luôn có chữ và icon đi kèm màu, không để màu là tín hiệu duy nhất. Giấy đã xác nhận đi
     *  hai nấc: "AI xác nhận" (xanh dương, còn chờ Nhân sự) → "Phòng nhân sự đã xác nhận" (xanh lá). Giấy AI từ chối
     *  gắn thêm nhãn "AI" để phân biệt với giấy Admin chốt tay. */
    function extStatusBadge(c) {
        const approvedLabel = c.hrConfirmedAt ? t('certificate.extHrConfirmed', 'Phòng nhân sự đã xác nhận')
            : c.isAiReviewed ? t('certificate.extApprovedAi', 'AI xác nhận')
            : t('certificate.extApproved', 'Đã xác nhận');
        const [tone, label, icon] = {
            Pending: ['orange', t('certificate.extPending', 'AI đang đối chiếu'), 'ph-robot'],
            Approved: [c.hrConfirmedAt ? 'green' : 'blue', approvedLabel, c.hrConfirmedAt ? 'ph-stamp' : 'ph-seal-check'],
            Rejected: ['red', t('certificate.extRejected', 'Bị từ chối'), 'ph-x-circle'],
        }[c.status] || ['gray', c.status, 'ph-question'];
        const aiTag = c.status === 'Rejected' && c.isAiReviewed ? '<span class="ext-ai-tag">AI</span>' : '';
        return `<span class="badge ${tone} ext-badge"><i class="ph-fill ${icon}"></i>${esc(label)}${aiTag}</span>`;
    }

    /** Dòng nhỏ dưới huy hiệu trong bảng duyệt: người phòng Nhân sự đã xác nhận, hoặc Admin đã chốt tay, là ai và lúc
     *  nào — nhìn bảng là biết, khỏi mở từng giấy. Quyết định của AI thì không có tên người. */
    function extDeciderHtml(c) {
        const [name, at] = c.hrConfirmedAt ? [c.hrConfirmedByName, c.hrConfirmedAt]
            : (c.reviewedAt && !c.isAiReviewed) ? [c.reviewedByName, c.reviewedAt]
            : [null, null];
        return name ? `<div class="small muted" style="margin-top:4px">${esc(t('certificate.extDecidedBy', 'bởi'))} ${esc(name)} · ${formatDate(at, true)}</div>` : '';
    }

    /** Mọi nút của phần chứng nhận bên ngoài gom vào MỘT handler ủy quyền trên #content — các thẻ / bảng được
     *  vẽ lại liên tục (đổi bộ lọc, duyệt xong), gắn thẳng vào nút thì nút mới sinh ra không có handler. */
    function bindExternalFeature() {
        content.addEventListener('click', (e) => {
            const target = e.target.closest('[data-ext-add], [data-ext-view], [data-ext-delete], [data-rg-print]');
            if (!target) return;
            if (target.hasAttribute('data-ext-add')) openAddModal();
            else if (target.dataset.extView) openExternalViewer(+target.dataset.extView);
            else if (target.dataset.extDelete) deleteExternal(+target.dataset.extDelete);
            else if (target.hasAttribute('data-rg-print')) printRegister();
        });

        document.querySelectorAll('[data-ext-mode]').forEach(button =>
            button.addEventListener('click', () => setScanMode(button.dataset.extMode)));
        document.getElementById('ext-form').addEventListener('submit', submitExternal);
        document.getElementById('ext-issued').max = dateInputValue(new Date());
        document.getElementById('ext-issued').addEventListener('change', (e) => {
            document.getElementById('ext-expired').min = e.target.value;
        });

        // Hộp thoại đóng bằng bất kỳ đường nào (nút ×, nút Hủy, bấm ra nền, gửi xong) cũng phải TẮT camera —
        // không thì đèn camera của máy vẫn sáng sau khi hộp thoại đã biến mất. Theo dõi chính lớp "open"
        // thay vì vá từng nút đóng.
        observeClose('modal-ext-add', () => { stopCamera(); clearPickedFile(); capturedCanvas = null; resetScan(); });
        observeClose('modal-ext-view', () => {
            if (viewFileUrl) { URL.revokeObjectURL(viewFileUrl); viewFileUrl = null; }
            document.getElementById('ext-view-file').innerHTML = '';
        });
    }

    function observeClose(modalId, onClose) {
        const modal = document.getElementById(modalId);
        let wasOpen = false;
        new MutationObserver(() => {
            const isOpen = modal.classList.contains('open');
            if (wasOpen && !isOpen) onClose();
            wasOpen = isOpen;
        }).observe(modal, { attributes: true, attributeFilter: ['class'] });
    }

    /** "yyyy-MM-dd" theo giờ địa phương cho ô ngày (toISOString đổi sang UTC là lùi một ngày ở múi +7). */
    function dateInputValue(value) {
        const d = new Date(value);
        return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    }

    function fileSizeText(byte) {
        return byte >= 1024 * 1024 ? `${(byte / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(byte / 1024))} KB`;
    }

    // ==================== HỘP THOẠI "THÊM CHỨNG NHẬN" ====================

    function openAddModal() {
        document.getElementById('ext-form').reset();
        capturedCanvas = null;
        clearPickedFile();
        Object.keys(aiFilledValue).forEach(key => delete aiFilledValue[key]);
        resetScan();
        openModal('modal-ext-add');
        // Máy không có camera dùng được (máy bàn không webcam) thì mở thẳng vào tải tệp.
        setScanMode(navigator.mediaDevices?.getUserMedia ? 'camera' : 'file');
        setTimeout(() => document.getElementById('ext-title').focus(), 50);
    }

    function setScanMode(mode) {
        scanMode = mode;
        document.querySelectorAll('[data-ext-mode]').forEach(button => {
            button.classList.toggle('on', button.dataset.extMode === mode);
            button.setAttribute('aria-selected', String(button.dataset.extMode === mode));
        });
        stopCamera();
        // Đổi cách lấy tệp là bỏ tệp của cách kia — lượt AI đọc tệp đó cũng thôi.
        resetScan();
        if (mode === 'camera') {
            clearPickedFile();
            if (capturedCanvas) { renderCaptured(); startScan(); } else startCamera();
        } else {
            capturedCanvas = null;
            if (pickedFile) { renderPickedFile(); startScan(); } else renderFilePicker();
        }
    }

    function stage() { return document.getElementById('ext-stage'); }

    // ---------- Camera ----------

    async function startCamera() {
        // Trình duyệt chỉ mở camera trên trang HTTPS (hoặc localhost). Hệ thống chạy http trong mạng nội bộ thì
        // getUserMedia không có — quay về ô chọn tệp có capture: trên điện thoại nó mở thẳng ứng dụng camera.
        if (!navigator.mediaDevices?.getUserMedia || !window.isSecureContext) {
            renderCameraFallback(t('certificate.extCamInsecure', 'Trình duyệt chỉ cho mở camera trực tiếp trên kết nối bảo mật (HTTPS). Dùng nút dưới đây để chụp bằng ứng dụng camera của máy.'));
            return;
        }

        stage().innerHTML = `<div class="scan-frame"><div class="scan-wait"><i class="ph ph-camera"></i>${esc(t('certificate.extCamOpening', 'Đang mở camera...'))}</div></div>`;
        let stream;
        try {
            stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: { ideal: 'environment' }, width: { ideal: 1920 }, height: { ideal: 1080 } },
                audio: false,
            });
        } catch (ex) {
            // Trong lúc chờ quyền camera mà người dùng đã chuyển sang tải tệp (hoặc đóng hộp thoại) thì thôi — vẽ
            // khung camera lúc này là đè mất ảnh họ vừa chọn.
            if (scanMode !== 'camera' || !document.getElementById('modal-ext-add').classList.contains('open')) return;
            renderCameraFallback(ex?.name === 'NotAllowedError'
                ? t('certificate.extCamDenied', 'Bạn chưa cho phép dùng camera. Cho phép trong thanh địa chỉ của trình duyệt, hoặc dùng nút dưới đây.')
                : t('certificate.extCamNone', 'Không mở được camera trên máy này. Bạn có thể chụp bằng ứng dụng camera hoặc chuyển sang tải tệp.'));
            return;
        }

        // Người dùng đã đóng hộp thoại / đổi sang tải tệp trong lúc chờ quyền camera thì trả camera lại ngay.
        if (scanMode !== 'camera' || !document.getElementById('modal-ext-add').classList.contains('open')) {
            stream.getTracks().forEach(track => track.stop());
            return;
        }
        cameraStream = stream;

        stage().innerHTML = `
            <div class="scan-frame">
                <video id="scan-video" playsinline muted autoplay></video>
                <div class="scan-guide" id="scan-guide"><i class="tl"></i><i class="tr"></i><i class="bl"></i><i class="br"></i></div>
                <div class="scan-tip">${esc(t('certificate.extCamTip', 'Đặt giấy vừa khung, đủ sáng, không bị lóa'))}</div>
            </div>
            <div class="scan-bar">
                <button type="button" class="scan-side" id="scan-orient">
                    <i class="ph ph-arrows-counter-clockwise"></i>${esc(guideLandscape ? t('certificate.extLandscape', 'Khổ ngang') : t('certificate.extPortrait', 'Khổ dọc'))}</button>
                <button type="button" class="scan-shutter" id="scan-shot" aria-label="${esc(t('certificate.extShoot', 'Chụp'))}"></button>
                <button type="button" class="scan-side" data-ext-mode-switch>
                    <i class="ph ph-file-arrow-up"></i>${esc(t('certificate.extUseFile', 'Tải tệp'))}</button>
            </div>`;

        const video = document.getElementById('scan-video');
        video.srcObject = cameraStream;
        video.addEventListener('loadedmetadata', layoutGuide);
        video.play().catch(() => { /* autoplay bị chặn — muted + playsinline thường đủ để không xảy ra */ });
        document.getElementById('scan-shot').addEventListener('click', captureFrame);
        document.getElementById('scan-orient').addEventListener('click', () => {
            guideLandscape = !guideLandscape;
            document.getElementById('scan-orient').lastChild.textContent = guideLandscape
                ? t('certificate.extLandscape', 'Khổ ngang') : t('certificate.extPortrait', 'Khổ dọc');
            layoutGuide();
        });
        stage().querySelector('[data-ext-mode-switch]').addEventListener('click', () => setScanMode('file'));
    }

    function stopCamera() {
        cameraStream?.getTracks().forEach(track => track.stop());
        cameraStream = null;
    }

    function renderCameraFallback(message) {
        stage().innerHTML = `
            <div class="ext-drop" style="cursor:default">
                <i class="ph ph-camera-slash"></i>
                <span>${esc(message)}</span>
                <label class="btn sm" style="margin-top:6px">
                    <i class="ph ph-camera"></i> ${esc(t('certificate.extOpenCamApp', 'Mở camera của máy'))}
                    <input type="file" accept="image/*" capture="environment" id="scan-native" hidden>
                </label>
            </div>`;
        document.getElementById('scan-native').addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (!file) return;
            // Ảnh chụp từ ứng dụng camera đi tiếp như một ảnh vừa chụp: vẽ lên canvas để còn "làm rõ chữ".
            const image = new Image();
            image.onload = () => {
                const canvas = document.createElement('canvas');
                const scale = Math.min(1, 2400 / Math.max(image.naturalWidth, image.naturalHeight));
                canvas.width = Math.round(image.naturalWidth * scale);
                canvas.height = Math.round(image.naturalHeight * scale);
                canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
                URL.revokeObjectURL(image.src);
                capturedCanvas = canvas;
                enhanceCapture = false;
                renderCaptured();
                startScan();
            };
            image.onerror = () => toastErr(t('certificate.extBadImage', 'Không đọc được ảnh vừa chụp.'));
            image.src = URL.createObjectURL(file);
        });
    }

    /** Vùng khung ngắm trong toạ độ điểm ảnh của video: 92% cạnh bị giới hạn, đúng tỷ lệ khổ giấy A (√2). */
    function guideRect(videoWidth, videoHeight) {
        const ratio = guideLandscape ? Math.SQRT2 : 1 / Math.SQRT2;
        let width = videoWidth * 0.92;
        let height = width / ratio;
        if (height > videoHeight * 0.92) { height = videoHeight * 0.92; width = height * ratio; }
        return { x: (videoWidth - width) / 2, y: (videoHeight - height) / 2, width, height };
    }

    function layoutGuide() {
        const video = document.getElementById('scan-video');
        const guide = document.getElementById('scan-guide');
        if (!video?.videoWidth || !guide) return;
        // Video hiện đúng tỷ lệ gốc (width 100%, height auto) nên phần trăm trên khung = phần trăm trên ảnh.
        const r = guideRect(video.videoWidth, video.videoHeight);
        Object.assign(guide.style, {
            left: `${r.x / video.videoWidth * 100}%`, top: `${r.y / video.videoHeight * 100}%`,
            width: `${r.width / video.videoWidth * 100}%`, height: `${r.height / video.videoHeight * 100}%`,
        });
    }

    function captureFrame() {
        const video = document.getElementById('scan-video');
        if (!video?.videoWidth) return;
        const r = guideRect(video.videoWidth, video.videoHeight);
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(r.width);
        canvas.height = Math.round(r.height);
        // Chỉ giữ phần trong khung ngắm: bỏ mặt bàn, ngón tay cầm giấy... — đúng như bản quét.
        canvas.getContext('2d').drawImage(video, r.x, r.y, r.width, r.height, 0, 0, canvas.width, canvas.height);
        stopCamera();
        capturedCanvas = canvas;
        enhanceCapture = false;
        renderCaptured();
        startScan();
    }

    function renderCaptured() {
        stage().innerHTML = `
            <div class="scan-frame"><canvas id="scan-result"></canvas></div>
            <div class="ext-preview-bar">
                <label class="ext-toggle"><input type="checkbox" id="scan-enhance" ${enhanceCapture ? 'checked' : ''}>
                    ${esc(t('certificate.extEnhance', 'Làm rõ chữ (giống bản scan)'))}</label>
                <span class="file"></span>
                <button type="button" class="btn ghost sm" id="scan-retake"><i class="ph ph-arrow-counter-clockwise"></i> ${esc(t('certificate.extRetake', 'Chụp lại'))}</button>
            </div>`;
        drawCaptured();
        // "Làm rõ chữ" đổi ảnh sẽ gửi đi → AI đọc lại đúng ảnh đó (ảnh rõ hơn cũng hay đọc đúng hơn).
        document.getElementById('scan-enhance').addEventListener('change', (e) => { enhanceCapture = e.target.checked; drawCaptured(); startScan(); });
        document.getElementById('scan-retake').addEventListener('click', () => { capturedCanvas = null; resetScan(); startCamera(); });
    }

    function drawCaptured() {
        const canvas = document.getElementById('scan-result');
        if (!canvas || !capturedCanvas) return;
        canvas.width = capturedCanvas.width;
        canvas.height = capturedCanvas.height;
        const context = canvas.getContext('2d');
        context.drawImage(capturedCanvas, 0, 0);
        if (enhanceCapture) enhanceDocument(context, canvas.width, canvas.height);
    }

    /** "Làm rõ chữ": kéo giãn độ sáng sao cho 2% điểm tối nhất thành đen, 2% sáng nhất thành trắng — nền giấy
     *  ngả vàng/xám dưới đèn trở về trắng, chữ đậm lên. Cùng một phép biến đổi cho cả ba kênh màu nên con dấu
     *  đỏ vẫn đỏ (đổi sang ảnh xám thì mất màu dấu — thứ người duyệt hay nhìn nhất). */
    function enhanceDocument(context, width, height) {
        const image = context.getImageData(0, 0, width, height);
        const data = image.data;
        const histogram = new Uint32Array(256);
        for (let i = 0; i < data.length; i += 4)
            histogram[(data[i] * 299 + data[i + 1] * 587 + data[i + 2] * 114) / 1000 | 0]++;

        const total = width * height;
        let low = 0, high = 255, sum = 0;
        while (low < 255 && (sum += histogram[low]) < total * 0.02) low++;
        sum = 0;
        while (high > 0 && (sum += histogram[high]) < total * 0.02) high--;
        if (high - low < 30) return;   // ảnh gần như một màu — kéo giãn chỉ làm nhiễu

        const scale = 255 / (high - low);
        for (let i = 0; i < data.length; i += 4) {
            data[i] = Math.min(255, Math.max(0, (data[i] - low) * scale));
            data[i + 1] = Math.min(255, Math.max(0, (data[i + 1] - low) * scale));
            data[i + 2] = Math.min(255, Math.max(0, (data[i + 2] - low) * scale));
        }
        context.putImageData(image, 0, 0);
    }

    // ---------- Tải tệp ----------

    function renderFilePicker() {
        stage().innerHTML = `
            <label class="ext-drop" id="ext-drop">
                <i class="ph-duotone ph-file-arrow-up"></i>
                <b>${esc(t('certificate.extDropTitle', 'Kéo thả hoặc bấm để chọn tệp'))}</b>
                <span>${esc(t('certificate.extDropHint', 'PDF hoặc ảnh JPG, PNG, WEBP — tối đa 15 MB'))}</span>
                <input type="file" id="ext-file" accept="application/pdf,image/jpeg,image/png,image/webp" hidden>
            </label>`;
        const drop = document.getElementById('ext-drop');
        document.getElementById('ext-file').addEventListener('change', (e) => acceptFile(e.target.files[0]));
        drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('drag'); });
        drop.addEventListener('dragleave', () => drop.classList.remove('drag'));
        drop.addEventListener('drop', (e) => {
            e.preventDefault();
            drop.classList.remove('drag');
            acceptFile(e.dataTransfer.files[0]);
        });
    }

    function acceptFile(file) {
        if (!file) return;
        if (!acceptedTypeList.includes(file.type)) {
            toastErr(t('certificate.extBadType', 'Chỉ nhận tệp PDF hoặc ảnh JPG, PNG, WEBP.'));
            return;
        }
        if (file.size > maxFileByte) {
            toastErr(t('certificate.extTooBig', 'Tệp vượt quá 15 MB.'));
            return;
        }
        clearPickedFile();
        pickedFile = file;
        pickedFileUrl = URL.createObjectURL(file);
        renderPickedFile();
        startScan();
    }

    function clearPickedFile() {
        if (pickedFileUrl) URL.revokeObjectURL(pickedFileUrl);
        pickedFile = null;
        pickedFileUrl = null;
    }

    function renderPickedFile() {
        const isPdf = pickedFile.type === 'application/pdf';
        stage().innerHTML = `
            <div class="ext-preview">
                ${isPdf
                    ? `<iframe src="${pickedFileUrl}" title="${esc(pickedFile.name)}"></iframe>`
                    : `<div class="scan-frame"><img src="${pickedFileUrl}" alt="${esc(pickedFile.name)}"></div>`}
            </div>
            <div class="ext-preview-bar">
                <i class="ph ${isPdf ? 'ph-file-pdf' : 'ph-image'}"></i>
                <span class="file" title="${esc(pickedFile.name)}">${esc(pickedFile.name)} · ${fileSizeText(pickedFile.size)}</span>
                <button type="button" class="btn ghost sm" id="ext-file-change"><i class="ph ph-swap"></i> ${esc(t('certificate.extChangeFile', 'Chọn tệp khác'))}</button>
            </div>`;
        document.getElementById('ext-file-change').addEventListener('click', () => { clearPickedFile(); resetScan(); renderFilePicker(); });
    }

    // ---------- AI đọc giấy ----------

    /** Bỏ lượt đọc đang chạy (nếu có) và đưa ô AI về lời dặn ban đầu. */
    function resetScan() {
        scanSeq++;
        const controller = scanController;
        scanController = null;
        controller?.abort();
        scanFile = null;
        scanState = 'idle';
        clearInterval(scanTimer);
        renderAiBox('idle');
    }

    /** Có tệp mới (vừa chụp, bật/tắt "làm rõ chữ", vừa chọn) → AI đọc ngay. Lượt đọc ảnh cũ bị hủy: máy chủ AI
     *  chạy CPU chỉ đọc nổi từng ảnh một, để nó đọc nốt ảnh đã bỏ là bắt ảnh mới xếp hàng chờ. */
    async function startScan() {
        const seq = ++scanSeq;
        const previous = scanController;
        scanController = null;
        previous?.abort();

        // Xuất ảnh ra JPEG là việc bất đồng bộ — trong lúc chờ mà người dùng đổi ảnh lần nữa thì lượt này bỏ.
        const file = await fileToSend();
        if (seq !== scanSeq) return;
        if (!file) { resetScan(); return; }
        const controller = new AbortController();
        scanController = controller;
        scanFile = file;
        scanState = 'reading';

        const startedAt = Date.now();
        renderAiBox('reading', { second: 0 });
        clearInterval(scanTimer);
        scanTimer = setInterval(() => {
            if (scanController === controller) renderAiBox('reading', { second: Math.round((Date.now() - startedAt) / 1000) });
        }, 1000);

        const form = new FormData();
        form.append('file', file.blob, file.name);
        try {
            const result = await API.postFormSilent('/externalcertificate/scan', form, controller.signal);
            if (scanController !== controller) return;
            scanState = 'done';
            fillFromReading(result.reading);
            renderAiBox(result.problem ? 'problem' : 'done', result);
        } catch (ex) {
            if (ex?.name === 'AbortError' || scanController !== controller) return;
            scanState = 'error';
            renderAiBox(ex.status === 503 || ex.status === 0 ? 'unavailable' : 'failed', { message: ex.message });
        } finally {
            if (scanController === controller) {
                scanController = null;
                clearInterval(scanTimer);
            }
        }
    }

    /** Điền những gì AI đọc được — chỉ vào ô còn trống hoặc ô chính AI đã điền ở lượt trước; ô nhân viên tự gõ
     *  thì giữ nguyên (họ biết rõ giấy của họ hơn model). */
    function fillFromReading(reading) {
        const valueById = {
            'ext-title': reading.title || '',
            'ext-issuer': reading.issuer || '',
            'ext-no': reading.certificateNo || '',
            'ext-issued': reading.issuedAt ? dateInputValue(reading.issuedAt) : '',
            'ext-expired': reading.expiredAt ? dateInputValue(reading.expiredAt) : '',
        };
        Object.entries(valueById).forEach(([id, value]) => {
            const input = document.getElementById(id);
            if (input.value && input.value !== aiFilledValue[id]) return;
            input.value = value;
            aiFilledValue[id] = value;
        });
        document.getElementById('ext-expired').min = document.getElementById('ext-issued').value;
    }

    function renderAiBox(state, data = {}) {
        const box = document.getElementById('ext-ai');
        if (!box) return;
        box.className = `ext-ai ${{ done: 'ok', problem: 'bad', mismatch: 'bad', unavailable: 'warn', failed: 'warn' }[state] || ''}`;

        if (state === 'reading') {
            box.innerHTML = `<div class="spinner"></div><div>
                <b>${esc(t('certificate.extAiReading', 'AI đang đọc giấy chứng nhận...'))}</b> ${data.second ? `${data.second}s` : ''}<br>
                ${esc(t('certificate.extAiReadingHint', 'Máy chủ AI nội bộ — thường mất một đến vài phút. Thông tin đọc được sẽ tự điền vào bên dưới; bạn cũng có thể tự nhập và gửi luôn, AI đọc xong sẽ đối chiếu và báo kết quả.'))}</div>`;
        } else if (state === 'done') {
            const holder = data.reading?.holderName;
            box.innerHTML = `<i class="ph-fill ph-check-circle"></i><div>
                <b>${esc(t('certificate.extAiDone', 'AI đã đọc xong và điền sẵn thông tin — kiểm tra lại rồi bấm Gửi.'))}</b>
                ${holder ? `<span class="ext-ai-holder">${esc(t('certificate.extAiHolder', 'Họ tên trên giấy'))}: <b>${esc(holder)}</b>${data.isNameMatched
                    ? ` · ${esc(t('certificate.extAiNameOk', 'khớp tài khoản'))}` : ''}</span>` : ''}</div>`;
        } else if (state === 'problem') {
            box.innerHTML = `<i class="ph-fill ph-warning-octagon"></i><div>
                <b>${esc(t('certificate.extAiProblem', 'Giấy này sẽ không được xác nhận:'))}</b> ${esc(data.problem)}</div>`;
        } else if (state === 'mismatch') {
            box.innerHTML = `<i class="ph-fill ph-warning-octagon"></i><div>
                <b>${esc(t('certificate.extAiMismatch', 'AI đối chiếu thấy chỗ chưa khớp với giấy — sửa lại rồi gửi:'))}</b>
                <ul>${data.messageList.map(message => `<li>${esc(message)}</li>`).join('')}</ul></div>`;
        } else if (state === 'unavailable') {
            box.innerHTML = `<i class="ph-fill ph-cloud-slash"></i><div>${esc(data.message)}
                ${esc(t('certificate.extAiLater', 'Bạn cứ nhập thông tin rồi gửi — AI sẽ đối chiếu khi máy chủ AI sẵn sàng và báo kết quả cho bạn.'))}</div>`;
        } else if (state === 'failed') {
            box.innerHTML = `<i class="ph-fill ph-warning"></i><div>
                <b>${esc(t('certificate.extAiFailed', 'AI chưa đọc được giấy này:'))}</b> ${esc(data.message)}
                ${esc(t('certificate.extAiRetry', 'Chụp lại rõ hơn, hoặc tự nhập thông tin rồi gửi để AI đối chiếu lại sau.'))}</div>`;
        } else {
            box.innerHTML = `<i class="ph ph-sparkle"></i><div>${esc(t('certificate.extAiIdle', 'Chụp hoặc chọn tệp là AI đọc giấy ngay: tự điền thông tin bên dưới và đối chiếu với tài khoản của bạn. Khớp thì chứng nhận được xác nhận luôn, không phải chờ duyệt.'))}</div>`;
        }
    }

    // ---------- Gửi ----------

    /** Tệp sẽ gửi đi: ảnh vừa chụp (xuất JPEG, đã áp "làm rõ chữ" nếu bật) hoặc tệp đã chọn. */
    async function fileToSend() {
        if (scanMode === 'file') return pickedFile ? { blob: pickedFile, name: pickedFile.name } : null;
        if (!capturedCanvas) return null;
        const source = document.getElementById('scan-result') || capturedCanvas;
        const blob = await new Promise(resolve => source.toBlob(resolve, 'image/jpeg', 0.9));
        const now = new Date();
        const stamp = `${dateInputValue(now).replace(/-/g, '')}-${String(now.getHours()).padStart(2, '0')}${String(now.getMinutes()).padStart(2, '0')}`;
        return blob ? { blob, name: `chung-nhan-${stamp}.jpg` } : null;
    }

    async function submitExternal(e) {
        e.preventDefault();
        const title = document.getElementById('ext-title').value.trim();
        const issuer = document.getElementById('ext-issuer').value.trim();
        const issuedAt = document.getElementById('ext-issued').value;
        const expiredAt = document.getElementById('ext-expired').value;

        // Gửi ĐÚNG tệp AI vừa đọc (cùng dãy byte) để máy chủ nhận ra kết quả đọc sẵn và đối chiếu ngay.
        const file = scanFile || await fileToSend();
        if (!file) { toastErr(t('certificate.extNeedFile', 'Vui lòng chụp hoặc tải lên giấy chứng nhận.')); return; }
        if (!title) { toastErr(t('certificate.extNeedTitle', 'Vui lòng nhập tên chứng nhận.')); document.getElementById('ext-title').focus(); return; }
        if (!issuer) { toastErr(t('certificate.extNeedIssuer', 'Vui lòng nhập đơn vị cấp.')); document.getElementById('ext-issuer').focus(); return; }
        if (!issuedAt) { toastErr(t('certificate.extNeedIssued', 'Vui lòng nhập ngày cấp.')); document.getElementById('ext-issued').focus(); return; }
        if (expiredAt && expiredAt < issuedAt) { toastErr(t('certificate.extBadExpiry', 'Ngày hết hạn phải sau ngày cấp.')); return; }

        const form = new FormData();
        form.append('title', title);
        form.append('issuer', issuer);
        form.append('certificateNo', document.getElementById('ext-no').value.trim());
        form.append('issuedAt', issuedAt);
        if (expiredAt) form.append('expiredAt', expiredAt);
        form.append('note', document.getElementById('ext-note').value.trim());
        form.append('file', file.blob, file.name);

        const submitButton = document.getElementById('ext-submit');
        submitButton.disabled = true;
        try {
            const result = await API.postForm('/externalcertificate', form);
            toastOk(result?.message || t('certificate.extSent', 'Đã gửi chứng nhận.'));
            // Gửi khi AI còn đang đọc: để lượt đọc chạy tiếp (đóng hộp thoại sẽ hủy nó) — máy chủ đối chiếu nền bằng
            // chính lượt đọc này, khỏi đọc lại từ đầu.
            if (scanState === 'reading') scanController = null;
            closeModal('modal-ext-add');
            loadedTab.external = false;
            await loadExternalTab();
        } catch (ex) {
            // AI đọc giấy và thấy lệch: giấy chưa được lưu, hộp thoại vẫn mở — chỉ ra đúng ô nào lệch để sửa.
            const failedList = (ex.data?.aiResult?.checkList || []).filter(x => x.ok === false && x.message);
            if (failedList.length) {
                const messageList = failedList.map(x => x.message);
                if (failedList.some(x => !x.found)) messageList.push(t('certificate.extAiRetake', 'Hãy chụp lại rõ hơn: đủ sáng, không lóa, thấy trọn tờ giấy.'));
                renderAiBox('mismatch', { messageList });
                toastErr(t('certificate.extAiMismatchToast', 'Thông tin chưa khớp với giấy — xem chi tiết trong hộp thoại.'));
            } else toastErr(ex.message);
        } finally {
            submitButton.disabled = false;
        }
    }

    async function deleteExternal(id) {
        const c = externalById.get(id);
        if (!await confirmDialog(t('certificate.extDeleteConfirm', 'Xóa chứng nhận "{title}"? Tệp quét đi kèm cũng bị xóa.', { title: c?.title || '' }),
            { danger: true, okText: t('common.delete', 'Xóa') })) return;
        try {
            const result = await API.del(`/externalcertificate/${id}`);
            toastOk(result?.message || t('certificate.extDeleted', 'Đã xóa.'));
            externalById.delete(id);
            if (loadedTab.external) { myExternalList = myExternalList.filter(x => x.id !== id); renderExternalTab(); }
            if (loadedTab.review) reloadReview();
            if (Auth.isAdmin) refreshReviewCount();
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== XEM TỆP / DUYỆT ====================

    async function openExternalViewer(id) {
        const c = externalById.get(id);
        if (!c) return;
        // Admin không phải duyệt, nhưng sửa được quyết định của AI: xác nhận giấy AI từ chối nhầm / chưa đọc được,
        // thu hồi giấy AI đã xác nhận (AI không phân biệt được giấy giả).
        const canApprove = Auth.isAdmin && c.status !== 'Approved';
        const canReject = Auth.isAdmin && c.status !== 'Rejected';
        // Phòng Nhân sự chốt bước cuối cho giấy đã qua đối chiếu — trừ giấy của chính mình (máy chủ cũng chặn).
        const canHrConfirm = isHrReviewer && c.status === 'Approved' && !c.hrConfirmedAt && c.userId !== Auth.profile?.id;

        document.getElementById('ext-view-info').innerHTML = `
            <h4>${esc(c.title)}</h4>
            <div style="margin-bottom:12px">${extStatusBadge(c)}</div>
            <dl>
                <dt>${esc(t('certificate.extColEmployee', 'Nhân viên'))}</dt>
                <dd>${esc(c.userFullName)}<span class="small muted" style="display:block;font-weight:500">${esc(c.employeeCode)}${c.departmentName ? ' · ' + esc(c.departmentName) : ''}</span></dd>
                <dt>${esc(t('certificate.extColIssuer', 'Đơn vị cấp'))}</dt><dd>${esc(c.issuer)}</dd>
                ${c.certificateNo ? `<dt>${esc(t('certificate.extColNo', 'Số hiệu'))}</dt><dd>${esc(c.certificateNo)}</dd>` : ''}
                <dt>${esc(t('certificate.extColIssued', 'Ngày cấp'))}</dt><dd>${formatDate(c.issuedAt)}</dd>
                <dt>${esc(t('certificate.extColExpired', 'Hết hạn'))}</dt><dd>${c.expiredAt ? formatDate(c.expiredAt) : esc(t('certificate.extNoExpiry', 'Không thời hạn'))}</dd>
                <dt>${esc(t('certificate.extColSent', 'Gửi lúc'))}</dt><dd>${formatDate(c.createdAt, true)}</dd>
                ${c.note ? `<dt>${esc(t('certificate.extColNote', 'Ghi chú'))}</dt><dd>${esc(c.note)}</dd>` : ''}
                ${c.reviewedAt ? `<dt>${esc(t('certificate.extColReviewer', 'Người xác nhận'))}</dt><dd>${c.isAiReviewed
                    ? `AI${c.aiResult?.model ? ` <span class="small muted" style="font-weight:500">(${esc(c.aiResult.model)})</span>` : ''}`
                    : esc(c.reviewedByName || '—')} · ${formatDate(c.reviewedAt, true)}</dd>` : ''}
                ${c.hrConfirmedAt ? `<dt>${esc(t('certificate.extColHrConfirmer', 'Nhân sự xác nhận'))}</dt>
                    <dd>${esc(c.hrConfirmedByName || '—')} · ${formatDate(c.hrConfirmedAt, true)}</dd>` : ''}
            </dl>
            ${c.status === 'Rejected' && c.rejectReason ? `<div class="ext-view-reason"><b>${esc(t('certificate.extRejectReason', 'Lý do từ chối'))}:</b> ${esc(c.rejectReason)}</div>` : ''}
            ${aiReadingHtml(c.aiResult)}`;

        document.getElementById('ext-view-foot').innerHTML = `
            <button type="button" class="btn ghost" id="ext-view-download"><i class="ph ph-download-simple"></i> ${esc(t('certificate.extDownload', 'Tải tệp'))}</button>
            <span style="flex:1"></span>
            ${canReject ? `<button type="button" class="btn danger" id="ext-reject"><i class="ph ph-x"></i> ${esc(c.status === 'Approved'
                ? t('certificate.extRevoke', 'Thu hồi xác nhận') : t('certificate.extReject', 'Từ chối'))}</button>` : ''}
            ${canApprove ? `<button type="button" class="btn" id="ext-approve"><i class="ph ph-check"></i> ${esc(t('certificate.extApprove', 'Xác nhận'))}</button>` : ''}
            ${canHrConfirm ? `<button type="button" class="btn" id="ext-hr-confirm"><i class="ph ph-stamp"></i> ${esc(t('certificate.extHrConfirm', 'Phòng nhân sự xác nhận'))}</button>` : ''}
            ${!canApprove && !canReject && !canHrConfirm ? `<button type="button" class="btn ghost" onclick="closeModal('modal-ext-view')">${esc(t('common.close', 'Đóng'))}</button>` : ''}`;

        document.getElementById('ext-view-download').addEventListener('click', () =>
            API.download(`/externalcertificate/${id}/file`, c.fileName).catch(ex => toastErr(ex.message)));
        document.getElementById('ext-approve')?.addEventListener('click', () => approveExternal(id));
        document.getElementById('ext-hr-confirm')?.addEventListener('click', () => hrConfirmExternal(id));
        document.getElementById('ext-reject')?.addEventListener('click', () => rejectExternal(id, c.status === 'Approved'));

        const fileBox = document.getElementById('ext-view-file');
        fileBox.innerHTML = skeletonHtml('block', { height: '320px' });
        openModal('modal-ext-view');

        try {
            const blob = await API.blob(`/externalcertificate/${id}/file`);
            if (viewFileUrl) URL.revokeObjectURL(viewFileUrl);
            viewFileUrl = URL.createObjectURL(blob);
            fileBox.innerHTML = c.contentType === 'application/pdf'
                ? `<iframe src="${viewFileUrl}" title="${esc(c.fileName)}"></iframe>`
                : `<img src="${viewFileUrl}" alt="${esc(c.title)}">`;
        } catch (ex) {
            fileBox.innerHTML = emptyHtml(ex.message, '<i class="ph ph-file-x"></i>');
        }
    }

    /** "AI đọc được trên giấy": từng mục kèm dấu ✓ khớp / ✗ lệch / – không so được — nhân viên thấy vì sao bị
     *  từ chối, Admin thấy AI đã dựa vào đâu mà xác nhận. */
    function aiReadingHtml(ai) {
        if (!ai) return '';
        const checkByField = Object.fromEntries((ai.checkList || []).map(x => [x.field, x]));
        const rowList = [
            ['document', t('certificate.extDocType', 'Loại giấy'), ai.documentType],
            ['name', t('certificate.extAiHolder', 'Họ tên trên giấy'), ai.holderName],
            ['title', t('certificate.extColCertificate', 'Chứng nhận'), ai.title],
            ['issuer', t('certificate.extColIssuer', 'Đơn vị cấp'), ai.issuer],
            ['no', t('certificate.extColNo', 'Số hiệu'), ai.certificateNo],
            ['issued', t('certificate.extColIssued', 'Ngày cấp'), ai.issuedAt ? formatDate(ai.issuedAt) : null],
            ['expired', t('certificate.extColExpired', 'Hết hạn'), ai.expiredAt ? formatDate(ai.expiredAt) : null],
        ];
        const mark = (ok) => ok === true ? '<i class="ph-bold ph-check ok"></i>'
            : ok === false ? '<i class="ph-bold ph-x bad"></i>' : '<i class="ph ph-minus skip"></i>';
        return `
            <div class="ext-ai-read">
                <h5><i class="ph ph-sparkle"></i> ${esc(t('certificate.extAiSection', 'AI đọc được trên giấy'))}</h5>
                <ul>${rowList.map(([field, label, value]) => `
                    <li>${mark(checkByField[field]?.ok)}<span>${esc(label)}</span>${value
                        ? `<b>${esc(value)}</b>` : `<span>${esc(t('certificate.extAiNotRead', 'không có / không đọc được'))}</span>`}</li>`).join('')}
                </ul>
                ${ai.note ? `<div class="small muted" style="margin-top:6px">${esc(ai.note)}</div>` : ''}
            </div>`;
    }

    async function approveExternal(id) {
        try {
            const result = await API.post(`/externalcertificate/${id}/approve`);
            toastOk(result?.message || t('certificate.extApprovedToast', 'Đã xác nhận.'));
            afterReview();
        } catch (ex) { toastErr(ex.message); }
    }

    async function rejectExternal(id, isRevoke = false) {
        const label = isRevoke ? t('certificate.extRevoke', 'Thu hồi xác nhận') : t('certificate.extReject', 'Từ chối');
        const reason = await promptDialog(isRevoke
            ? t('certificate.extRevokePrompt', 'Lý do thu hồi xác nhận (nhân viên sẽ đọc được lý do này):')
            : t('certificate.extRejectPrompt', 'Lý do từ chối (nhân viên sẽ đọc được lý do này):'), {
            title: label, okText: label, danger: true,
            placeholder: t('certificate.extRejectPh', 'VD: Ảnh mờ, không đọc được số hiệu'), maxLength: 1000,
        });
        if (reason === null || reason === undefined) return;
        if (reason.trim().length < 3) { toastErr(t('certificate.extNeedReason', 'Vui lòng nhập lý do từ chối.')); return; }
        try {
            const result = await API.post(`/externalcertificate/${id}/reject`, { reason: reason.trim() });
            toastOk(result?.message || t('certificate.extRejectedToast', 'Đã từ chối.'));
            afterReview();
        } catch (ex) { toastErr(ex.message); }
    }

    async function hrConfirmExternal(id) {
        const button = document.getElementById('ext-hr-confirm');
        if (button) button.disabled = true;
        try {
            const result = await API.post(`/externalcertificate/${id}/hr-confirm`);
            toastOk(result?.message || t('certificate.extHrConfirmedToast', 'Phòng nhân sự đã xác nhận chứng nhận.'));
            afterReview();
        } catch (ex) {
            toastErr(ex.message);
            if (button) button.disabled = false;
        }
    }

    function afterReview() {
        closeModal('modal-ext-view');
        refreshReviewCount();
        if (loadedTab.review) reloadReview();
        // Giấy vừa xác nhận phải có mặt trong danh sách in ở lần mở tiếp theo.
        if (loadedTab.register) reloadRegister();
    }

    // ==================== DUYỆT CHỨNG NHẬN (Admin + phòng Nhân sự) ====================

    async function ensureDepartments() {
        if (allDepartmentList) return;
        allDepartmentList = await API.getSilent('/department?isActive=true').catch(() => []);
    }

    /** Phòng ban lọc được ở thẻ In danh sách: Admin và phòng Nhân sự cả công ty; cấp trưởng chỉ các phòng mình quản
     *  (đã tải sẵn ở đầu trang) — kể cả "Chưa gắn phòng ban" (id 0) mà Tổng Giám Đốc có thể thấy. */
    function registerDepartmentList() {
        return isWholeCompanyPrinter() ? allDepartmentList : departmentList.map(d => ({ id: d.departmentId, name: d.name }));
    }

    function isWholeCompanyPrinter() { return Auth.isAdmin || isHrReviewer; }

    function departmentOptionHtml(allLabel, optionList = allDepartmentList) {
        return `<option value="">${esc(allLabel)}</option>`
            + optionList.map(d => `<option value="${d.id}">${esc(d.name)}</option>`).join('');
    }

    async function loadReviewTab() {
        loadedTab.review = true;
        const panel = document.getElementById('tab-review');
        panel.innerHTML = skeletonHtml('table', { rows: 4, cols: 6 });
        await ensureDepartments();
        panel.innerHTML = `
            <div class="card">
                <div class="card-head">
                    <h3><i class="ph ph-scan"></i> ${esc(t('certificate.reviewTitle', 'Chứng nhận bên ngoài của nhân viên'))}</h3>
                    <div class="ext-filter">
                        <select id="rv-status" aria-label="${esc(t('certificate.extColStatus', 'Trạng thái'))}">
                            <option value="">${esc(t('certificate.allStatus', 'Mọi trạng thái'))}</option>
                            <option value="AwaitingHr">${esc(t('certificate.extAwaitingHr', 'Chờ Nhân sự xác nhận'))}</option>
                            <option value="Approved">${esc(t('certificate.extApproved', 'Đã xác nhận'))}</option>
                            <option value="Rejected">${esc(t('certificate.extRejected', 'Bị từ chối'))}</option>
                            <option value="Pending">${esc(t('certificate.extPending', 'AI đang đối chiếu'))}</option>
                        </select>
                        <select id="rv-dept" aria-label="${esc(t('certificate.department', 'Phòng ban'))}">${departmentOptionHtml(t('certificate.allDepartment', 'Mọi phòng ban'))}</select>
                        <input type="search" id="rv-keyword" placeholder="${esc(t('certificate.searchPh', 'Tìm tên, mã NV, chứng nhận...'))}">
                    </div>
                </div>
                <div id="rv-body"></div>
            </div>`;
        // Nhân sự mở thẻ là để xác nhận — lọc sẵn những giấy đang chờ mình.
        if (!Auth.isAdmin) document.getElementById('rv-status').value = 'AwaitingHr';
        document.getElementById('rv-status').addEventListener('change', reloadReview);
        document.getElementById('rv-dept').addEventListener('change', reloadReview);
        let timer;
        document.getElementById('rv-keyword').addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(reloadReview, 350); });
        reloadReview();
    }

    async function reloadReview() {
        const body = document.getElementById('rv-body');
        if (!body) return;
        const query = new URLSearchParams();
        const status = document.getElementById('rv-status').value;
        const departmentId = document.getElementById('rv-dept').value;
        const keyword = document.getElementById('rv-keyword').value.trim();
        // "Chờ Nhân sự" không phải trạng thái lưu trong database: là giấy đã xác nhận mà Nhân sự chưa bấm.
        const isAwaitingHr = status === 'AwaitingHr';
        if (status) query.set('status', isAwaitingHr ? 'Approved' : status);
        if (departmentId) query.set('departmentId', departmentId);
        if (keyword) query.set('keyword', keyword);

        body.style.opacity = '.5';
        try {
            reviewList = await API.getSilent(`/externalcertificate?${query}`);
        } catch (ex) {
            body.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
            return;
        } finally {
            body.style.opacity = '';
        }
        reviewList.forEach(c => externalById.set(c.id, c));
        if (isAwaitingHr) reviewList = reviewList.filter(c => !c.hrConfirmedAt);

        body.innerHTML = reviewList.length ? `
            <div class="table-wrap"><table>
                <thead><tr>
                    <th>${esc(t('certificate.extColEmployee', 'Nhân viên'))}</th>
                    <th>${esc(t('certificate.extColCertificate', 'Chứng nhận'))}</th>
                    <th>${esc(t('certificate.extColIssued', 'Ngày cấp'))}</th>
                    <th>${esc(t('certificate.extColSent', 'Gửi lúc'))}</th>
                    <th>${esc(t('certificate.extColStatus', 'Trạng thái'))}</th>
                    <th class="text-right"></th>
                </tr></thead>
                <tbody>${reviewList.map(c => `
                    <tr>
                        <td><b>${esc(c.userFullName)}</b><div class="small muted">${esc(c.employeeCode)}${c.departmentName ? ' · ' + esc(c.departmentName) : ''}</div></td>
                        <td><b>${esc(c.title)}</b><div class="small muted">${esc(c.issuer)}${c.certificateNo ? ' · ' + esc(c.certificateNo) : ''}</div></td>
                        <td class="nowrap">${formatDate(c.issuedAt)}${c.expiredAt ? `<div class="small muted">→ ${formatDate(c.expiredAt)}</div>` : ''}</td>
                        <td class="nowrap small">${formatDate(c.createdAt, true)}</td>
                        <td>${extStatusBadge(c)}${extDeciderHtml(c)}</td>
                        <td class="text-right nowrap">
                            <button type="button" class="btn ghost sm" data-ext-view="${c.id}">
                                <i class="ph ph-eye"></i> ${esc(t('certificate.extView', 'Xem'))}</button>
                            ${Auth.isAdmin ? `<button type="button" class="btn ghost sm" data-ext-delete="${c.id}" title="${esc(t('common.delete', 'Xóa'))}" aria-label="${esc(t('common.delete', 'Xóa'))}"><i class="ph ph-trash"></i></button>` : ''}
                        </td>
                    </tr>`).join('')}</tbody>
            </table></div>`
            : emptyHtml(t('certificate.reviewEmpty', 'Không có chứng nhận nào ở mục này.'), '<i class="ph ph-check-circle"></i>');
    }

    // ==================== IN DANH SÁCH (Admin + cấp trưởng) ====================

    async function loadRegisterTab() {
        loadedTab.register = true;
        const panel = document.getElementById('tab-register');
        panel.innerHTML = skeletonHtml('block', { height: '320px' });
        await ensureDepartments();
        panel.innerHTML = `
            <div class="card no-print">
                <div class="card-body register-filter-row">
                    <div class="field">
                        <label for="rg-source">${esc(t('certificate.regSource', 'Nguồn'))}</label>
                        <select id="rg-source">
                            <option value="">${esc(t('certificate.regSourceAll', 'Nội bộ + bên ngoài'))}</option>
                            <option value="internal">${esc(t('certificate.regSourceInternal', 'Chỉ nội bộ'))}</option>
                            <option value="external">${esc(t('certificate.regSourceExternal', 'Chỉ bên ngoài'))}</option>
                        </select>
                    </div>
                    <div class="field">
                        <label for="rg-dept">${esc(t('certificate.department', 'Phòng ban'))}</label>
                        <select id="rg-dept">${departmentOptionHtml(t('certificate.allDepartment', 'Mọi phòng ban'), registerDepartmentList())}</select>
                    </div>
                    <div class="field"><label for="rg-from">${esc(t('certificate.regFrom', 'Cấp từ ngày'))}</label><input type="date" id="rg-from"></div>
                    <div class="field"><label for="rg-to">${esc(t('certificate.regTo', 'Đến ngày'))}</label><input type="date" id="rg-to"></div>
                    <div class="field grow">
                        <label for="rg-keyword">${esc(t('certificate.regSearch', 'Tìm kiếm'))}</label>
                        <input type="search" id="rg-keyword" placeholder="${esc(t('certificate.searchPh', 'Tìm tên, mã NV, chứng nhận...'))}">
                    </div>
                    <button type="button" class="btn" data-rg-print><i class="ph ph-printer"></i> ${esc(t('certificate.regPrint', 'In danh sách'))}</button>
                </div>
            </div>
            <div id="rg-sheet-wrap"></div>`;
        // Trưởng phòng / Giám Đốc chỉ quản một phòng: chọn sẵn để tờ in ghi đúng tên phòng chứ không phải "Mọi phòng ban".
        if (!isWholeCompanyPrinter() && singleDepartment) document.getElementById('rg-dept').value = String(departmentList[0].departmentId);
        ['rg-source', 'rg-dept', 'rg-from', 'rg-to'].forEach(id => document.getElementById(id).addEventListener('change', reloadRegister));
        let timer;
        document.getElementById('rg-keyword').addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(reloadRegister, 350); });
        reloadRegister();
    }

    function registerFilter() {
        return {
            source: document.getElementById('rg-source').value,
            departmentId: document.getElementById('rg-dept').value,
            from: document.getElementById('rg-from').value,
            to: document.getElementById('rg-to').value,
            keyword: document.getElementById('rg-keyword').value.trim(),
        };
    }

    async function reloadRegister() {
        const wrap = document.getElementById('rg-sheet-wrap');
        if (!wrap) return;
        const filter = registerFilter();
        const query = new URLSearchParams();
        Object.entries(filter).forEach(([key, value]) => { if (value) query.set(key, value); });

        // Đổi bộ lọc: giữ tờ cũ, mờ đi trong lúc chờ — không chớp khung xương, không nhảy bố cục.
        if (wrap.innerHTML) wrap.style.opacity = '.5';
        else wrap.innerHTML = skeletonHtml('block', { height: '360px' });
        try {
            registerRowList = await API.getSilent(`/certificate/register?${query}`);
        } catch (ex) {
            wrap.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
            return;
        } finally {
            wrap.style.opacity = '';
        }
        wrap.innerHTML = registerSheetHtml(filter);
    }

    /** Tờ danh sách — vừa là bản xem trước trên màn hình, vừa là đúng thứ được in ra (A4 ngang). Nhóm theo
     *  phòng ban, mỗi nhóm một dòng tiêu đề; số thứ tự chạy liên tục cả tờ. */
    function registerSheetHtml(filter) {
        const departmentName = filter.departmentId
            ? registerDepartmentList().find(d => d.id === +filter.departmentId)?.name : null;
        const sourceLabel = { internal: t('certificate.regSourceInternal', 'Chỉ nội bộ'), external: t('certificate.regSourceExternal', 'Chỉ bên ngoài') }[filter.source]
            || t('certificate.regSourceAll', 'Nội bộ + bên ngoài');
        const dateRange = filter.from || filter.to
            ? `${filter.from ? formatDate(filter.from) : '…'} – ${filter.to ? formatDate(filter.to) : '…'}`
            : t('certificate.regAllTime', 'mọi thời điểm');

        const internalCount = registerRowList.filter(x => x.source === 'Internal').length;
        const externalCount = registerRowList.length - internalCount;
        const peopleCount = new Set(registerRowList.map(x => x.userId)).size;

        let lastDepartment;
        let no = 0;
        const rowHtml = registerRowList.map(r => {
            const department = r.departmentName || t('certificate.regNoDepartment', 'Chưa gắn phòng ban');
            let group = '';
            if (department !== lastDepartment) {
                lastDepartment = department;
                const count = registerRowList.filter(x => (x.departmentName || t('certificate.regNoDepartment', 'Chưa gắn phòng ban')) === department).length;
                group = `<tr class="reg-group"><td colspan="9">${esc(department)} — ${count} ${esc(t('certificate.regCertUnit', 'chứng nhận'))}</td></tr>`;
            }
            no++;
            return group + `
                <tr>
                    <td class="c">${no}</td>
                    <td class="c">${esc(r.employeeCode)}</td>
                    <td><b>${esc(r.fullName)}</b>${r.positionName ? `<span class="muted2">${esc(r.positionName)}</span>` : ''}</td>
                    <td>${esc(r.title)}</td>
                    <td>${esc(r.issuer)}</td>
                    <td>${esc(r.certificateNo || '—')}</td>
                    <td class="c">${formatDate(r.issuedAt)}</td>
                    <td class="c">${r.expiredAt ? formatDate(r.expiredAt) : '—'}</td>
                    <td class="c"><span class="reg-src ${r.source === 'External' ? 'ext' : ''}">${esc(r.source === 'External'
                        ? t('certificate.regExternal', 'Bên ngoài') : t('certificate.regInternal', 'Nội bộ'))}</span></td>
                </tr>`;
        }).join('');

        const now = new Date();
        return `
                <div class="reg-sheet">
                    <div class="reg-head">
                        <div class="reg-brand">
                        <img src="/img/HoaBinh.png" alt="Hoà Bình Contruction Group">
                        <div class="reg-brand-text">
                        <b>Hoà Bình Contruction Group</b>
                        <span>${esc(t('certificate.regSystem', 'Hệ thống đào tạo nội bộ HB E-Learning'))}</span>
                    </div>
                </div>
                <div class="reg-meta">
                    ${esc(t('certificate.regPrintedAt', 'Ngày in'))}: ${formatDate(now, true)}<br>
                    ${esc(t('certificate.regPrintedBy', 'Người in'))}: ${esc(Auth.profile?.fullName || '')}
                </div>
                </div>
                <div class="reg-title">${esc(t('certificate.regTitle', 'DANH SÁCH CHỨNG NHẬN'))}</div>
                <div class="reg-sub">${esc(sourceLabel)} · ${esc(departmentName || t('certificate.allDepartment', 'Mọi phòng ban'))} · ${esc(t('certificate.regIssuedIn', 'Ngày cấp'))}: ${esc(dateRange)}${filter.keyword ? ` · "${esc(filter.keyword)}"` : ''}</div>
                <div class="reg-summary">
                    <span>${esc(t('certificate.regTotal', 'Tổng'))} <b>${registerRowList.length}</b></span>
                    <span>${esc(t('certificate.regInternal', 'Nội bộ'))} <b>${internalCount}</b></span>
                    <span>${esc(t('certificate.regExternal', 'Bên ngoài'))} <b>${externalCount}</b></span>
                    <span>${esc(t('certificate.regPeople', 'Nhân viên'))} <b>${peopleCount}</b></span>
                </div>
                ${registerRowList.length ? `
                    <table class="reg-table">
                        <thead><tr>
                            <th class="c">${esc(t('certificate.regNo', 'STT'))}</th>
                            <th class="c">${esc(t('certificate.regCode', 'Mã NV'))}</th>
                            <th>${esc(t('certificate.regName', 'Họ và tên'))}</th>
                            <th>${esc(t('certificate.extColCertificate', 'Chứng nhận'))}</th>
                            <th>${esc(t('certificate.extColIssuer', 'Đơn vị cấp'))}</th>
                            <th>${esc(t('certificate.extColNo', 'Số hiệu'))}</th>
                            <th class="c">${esc(t('certificate.extColIssued', 'Ngày cấp'))}</th>
                            <th class="c">${esc(t('certificate.extColExpired', 'Hết hạn'))}</th>
                            <th class="c">${esc(t('certificate.regSource', 'Nguồn'))}</th>
                        </tr></thead>
                        <tbody>${rowHtml}</tbody>
                    </table>`
                    : `<div class="reg-empty">${esc(t('certificate.regEmpty', 'Không có chứng nhận nào khớp bộ lọc.'))}</div>`}
                <div class="reg-sign">
                    <div>
                        <div class="date">&nbsp;</div>
                        <b>${esc(t('certificate.regApprover', 'Xác nhận của đơn vị'))}</b>
                        <div class="hint">${esc(t('certificate.regSignHint', '(Ký, ghi rõ họ tên)'))}</div>
                        <div class="space"></div>
                    </div>
                    <div>
                        <div class="date">${esc(t('certificate.regDateLine', 'Ngày {d} tháng {m} năm {y}', { d: now.getDate(), m: now.getMonth() + 1, y: now.getFullYear() }))}</div>
                        <b>${esc(t('certificate.regMaker', 'Người lập biểu'))}</b>
                        <div class="hint">${esc(t('certificate.regSignHint', '(Ký, ghi rõ họ tên)'))}</div>
                        <div class="space"></div>
                        <b style="text-transform:none">${esc(Auth.profile?.fullName || '')}</b>
                    </div>
                </div>
            </div>`;
    }

    /** In đúng tờ danh sách: bật lớp printing-register quanh lệnh in để CSS @media print giấu mọi thứ khác —
     *  nút "In / Lưu PDF" của từng chứng nhận nội bộ cũng dùng window.print() và không được bị ảnh hưởng. */
    function printRegister() {
        if (!registerRowList.length) {
            toastErr(t('certificate.regNothing', 'Danh sách đang trống — đổi bộ lọc rồi in lại.'));
            return;
        }
        const finish = () => {
            document.body.classList.remove('printing-register');
            window.removeEventListener('afterprint', finish);
        };
        document.body.classList.add('printing-register');
        // Gỡ lớp bằng afterprint chứ KHÔNG gỡ ngay sau window.print(): vài trình duyệt không chặn ở lệnh in mà
        // dựng bản in sau đó — gỡ sớm là in ra cả trang. Lỡ afterprint không tới thì lớp này cũng chỉ có tác dụng
        // khi in, không ảnh hưởng gì màn hình.
        window.addEventListener('afterprint', finish);
        window.print();
    }

    /** In đúng TỜ CHỨNG NHẬN đang mở. window.print() trần in cả trang phía sau hộp thoại (danh sách chứng nhận) nên
     *  bản in khác hẳn thứ đang xem; lớp printing-cert làm CSS @media print giấu mọi thứ ngoài hộp thoại này. Gỡ lớp
     *  bằng afterprint, không gỡ ngay sau lệnh in (cùng lý do với printRegister). */
    function printCertificate() {
        const finish = () => {
            document.body.classList.remove('printing-cert');
            window.removeEventListener('afterprint', finish);
        };
        // Khổ giấy A4 ngang đặt bằng @page tạm trong lúc in. Không dùng trang có tên (page: ...) trên chính tờ chứng nhận:
        // Chrome chèn thêm một trang trống phía sau phần tử đổi tên trang.
        const pageStyle = document.createElement('style');
        pageStyle.textContent = '@page { size: A4 landscape; margin: 8mm; }';
        document.head.appendChild(pageStyle);
        const finishAll = () => { pageStyle.remove(); finish(); };
        window.removeEventListener('afterprint', finish);
        document.body.classList.add('printing-cert');
        window.addEventListener('afterprint', finishAll, { once: true });
        window.print();
    }
    document.getElementById('btn-print-cert').addEventListener('click', printCertificate);
})();
