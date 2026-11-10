// File: wwwroot/pages/organization/organization.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    if (!Auth.require(['Admin'], 'organization.view')) return;
    mountLayout('organization', t('organization.phongBanViTriCongViec', 'Phòng ban & Vị trí công việc'), t('organization.coCauToChucDungDePhanBoNoiDung', 'Cơ cấu tổ chức dùng để phân bổ nội dung đào tạo'));
    // Chỉ hiện nút thao tác mà vai trò thực sự được cấp — tránh bấm vào rồi nhận lỗi 403 từ API.
    const canCreate = Auth.can('organization.create') ?? true;
    const canUpdate = Auth.can('organization.update') ?? true;
    const canDelete = Auth.can('organization.delete') ?? true;

    // Một nút chính + nút "…" cho nhập Excel / tải mẫu — việc thỉnh thoảng mới làm (cùng quy ước thanh trên).
    if (canCreate)
        setTopbarAction(`
            <button class="btn" id="btn-new"><i class="ph ph-plus"></i> <span data-i18n="organization.themMoiBtn">Thêm mới</span></button>
            <button class="topbar-more" type="button" id="btn-org-more" aria-haspopup="menu"
                    title="${esc(t('common.moreActions', 'Việc khác'))}"><i class="ph ph-dots-three-outline"></i></button>`);

    let currentTab = 'department', departmentList = [];

    // Phòng ban đang lọc ở tab Vị trí công việc: '' = tất cả, 'none' = vị trí chưa gán phòng ban nào, còn lại
    // là Id phòng ban (dạng CHUỖI, đúng như giá trị đọc ra từ ô chọn).
    //
    // Phải để NGOÀI load(): mỗi lần lọc là dựng lại toàn bộ thẻ, ô chọn cũ bị xóa cùng lựa chọn của nó — giữ
    // trong này thì vẽ lại xong vẫn chọn đúng phòng ban người dùng vừa lọc.
    let positionDepartmentId = '';

    document.querySelectorAll('.tabs button').forEach(button => {
        button.addEventListener('click', () => {
            currentTab = button.dataset.tab;
            showTab(currentTab, 'department');
            // Sang tab khác thì bỏ bộ lọc: quay lại tab Vị trí mà vẫn còn lọc cũ trong khi ô chọn vừa được
            // dựng lại ở trạng thái "Tất cả" là giao diện nói một đằng, bảng hiện một nẻo.
            positionDepartmentId = '';
            load();
        });
    });

    document.getElementById('btn-new')?.addEventListener('click', () => openForm());
    document.getElementById('org-form').addEventListener('submit', save);

    // Đường dẫn truyền vào dạng HÀM: hai tab dùng chung một cặp nút, phải đọc tab đang mở tại lúc BẤM chứ
    // không phải lúc gắn sự kiện — nếu không thì đứng ở tab Vị trí vẫn nhập vào Phòng ban.
    const excel = bindExcelImport({
        fileId: 'import-org-file',
        templatePath: () => `/${currentTab}/import/template`,
        importPath: () => `/${currentTab}/import`,
        fileName: () => (currentTab === 'department' ? 'Mau_Import_PhongBan.xlsx' : 'Mau_Import_ViTri.xlsx'),
        onDone: () => load(),
    });
    bindPopMenu(document.getElementById('btn-org-more'), () => [
        { icon: 'ph ph-upload-simple', label: t('import.importExcel', 'Import Excel'), onClick: excel?.pickFile },
        { icon: 'ph ph-download-simple', label: t('import.taiFileMau', 'Tải file mẫu'), onClick: excel?.downloadTemplate },
    ]);

    // Mở lại thẻ ghi trên địa chỉ trang (?tab=): tải lại trang, kể cả khi đổi ngôn ngữ, vẫn đứng nguyên thẻ đang xem.
    currentTab = tabFromUrl('department');
    showTab(currentTab, 'department');
    await load();

    async function load() {
        const box = document.getElementById(`tab-${currentTab}`);
        box.innerHTML = skeletonHtml('list', { count: 5, avatar: false });
        try {
            departmentList = await API.get('/department');
            const fullList = currentTab === 'department' ? departmentList : await API.get('/position');

            // Lọc ngay tại giao diện chứ không gọi lại API kèm tham số: cả danh sách vị trí công việc của một
            // công ty chỉ vài chục dòng và đã nằm sẵn trong tay, đi vòng qua máy chủ chỉ thêm một nhịp chờ.
            const list = currentTab === 'position' && positionDepartmentId
                ? fullList.filter(x => positionDepartmentId === 'none'
                    ? x.departmentId == null
                    : String(x.departmentId) === positionDepartmentId)
                : fullList;

            box.innerHTML = `
                <div class="card">
                    <div class="card-head"><h3>${list.length} ${currentTab === 'department' ? 'phòng ban' : t('organization.viTriCongViec', 'vị trí công việc')}</h3>
                        ${currentTab === 'position' ? `
                        <select id="filter-department" style="width:260px"
                                aria-label="${esc(t('organization.locTheoPhongBan', 'Lọc theo phòng ban'))}">
                            <option value="">${esc(t('course-detail.tatCaPhongBan', '— Tất cả phòng ban —'))}</option>
                            <option value="none">${esc(t('organization.chuaGanPhongBan', '— Chưa gán phòng ban —'))}</option>
                            ${departmentList.map(d => `<option value="${d.id}">${esc(d.name)}</option>`).join('')}
                        </select>` : ''}
                    </div>
                    <div class="table-wrap"><table>
                        <thead><tr><th data-i18n="organization.ma">Mã</th><th data-i18n="organization.ten2">Tên</th>
                            ${currentTab === 'position' ? '<th data-i18n="account.phongBan">Phòng ban</th>' : ''}
                            <th class="text-center">Nhân sự</th><th data-i18n="dashboard.colStatus">Trạng thái</th><th class="text-right"></th></tr></thead>
                        <tbody>${list.length ? list.map(x => `
                            <tr>
                                <td><code>${esc(x.code)}</code></td>
                                <td><b>${esc(x.name)}</b>${x.isHrDepartment ? ' <span data-i18n="organization.phongNhanSu" class="badge blue">Phòng Nhân sự</span>' : ''}</td>
                                ${currentTab === 'position' ? `<td class="small">${esc(x.departmentName || '—')}</td>` : ''}
                                <td class="text-center">${x.userCount}</td>
                                <td>${x.isActive ? '<span data-i18n="organization.hoatDong" class="badge green">Hoạt động</span>' : '<span data-i18n="organization.ngung" class="badge gray">Ngừng</span>'}</td>
                                <td class="text-right nowrap">
                                    ${canUpdate ? `<button data-i18n="common.edit" class="btn ghost sm" data-edit="${x.id}" style="background-color: yellow">Sửa</button>` : ''}
                                    ${canDelete ? `<button data-i18n="common.delete" class="btn ghost sm" data-del="${x.id}" style="background-color: red; color: white">Xóa</button>` : ''}
                                </td>
                            </tr>`).join('') : `<tr><td colspan="7">${emptyHtml(
                            currentTab === 'position' && positionDepartmentId
                                ? t('organization.khongCoViTriThuocPhongBanNay', 'Không có vị trí công việc nào thuộc phòng ban đang lọc.')
                                : t('common.empty', 'Chưa có dữ liệu.'),
                            '<i class="ph ph-buildings"></i>')}</td></tr>`}
                        </tbody>
                    </table></div>
                </div>`;

            // Gắn SAU khi đã đổ innerHTML — trước đó thẻ <select> chưa có trong trang.
            const filterSelect = document.getElementById('filter-department');
            if (filterSelect) {
                filterSelect.value = positionDepartmentId;
                enhanceSearchableSelect(filterSelect,
                    { placeholder: t('course.goTenPhongBan', 'Gõ tên hoặc mã phòng ban...') });
                filterSelect.addEventListener('change', () => {
                    positionDepartmentId = filterSelect.value;
                    load();
                });
            }

            box.querySelectorAll('[data-edit]').forEach(b =>
                b.addEventListener('click', () => openForm(list.find(x => x.id == b.dataset.edit))));

            box.querySelectorAll('[data-del]').forEach(b =>
                b.addEventListener('click', async () => {
                    const item = list.find(x => x.id == b.dataset.del);
                    const label = currentTab === 'position' ? 'vị trí công việc' : t('organization.phongBan', 'phòng ban');
                    if (!await confirmDialog(`Xóa ${label} "${item?.name || ''}"?`, { danger: true, okText: t('common.delete', 'Xóa') })) return;
                    try {
                        const r = await API.del(`/${currentTab}/${b.dataset.del}`);
                        toastOk(r.message);
                        load();
                    } catch (ex) { toastErr(ex.message); }
                }));
        } catch (ex) { box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>'); }
    }

    function openForm(item) {
        const isPosition = currentTab === 'position';
        document.getElementById('org-title').textContent =
            `${item ? 'Sửa' : t('organization.them', 'Thêm')} ${isPosition ? 'vị trí công việc' : t('organization.phongBan', 'phòng ban')}`;
        document.getElementById('o-kind').value = currentTab;
        document.getElementById('o-id').value = item?.id || '';
        document.getElementById('o-code').value = item?.code || '';
        // Mã do hệ thống tự sinh khi lưu lần đầu — chỉ hiện (dạng chỉ xem) lúc sửa, không có khi tạo mới.
        document.getElementById('o-code-row').classList.toggle('hidden', !item);
        document.getElementById('o-name').value = item?.name || '';
        document.getElementById('o-description').value = item?.description || '';
        document.getElementById('o-active').checked = item ? item.isActive : true;

        document.getElementById('o-hr-row').classList.toggle('hidden', isPosition);
        document.getElementById('o-hr').checked = !isPosition && !!item?.isHrDepartment;

        const field = document.getElementById('o-department-field');
        field.classList.toggle('hidden', !isPosition);
        document.getElementById('o-department').innerHTML = '<option data-i18n="organization.khongChon" value="">— Không chọn —</option>'
            + departmentList.map(d => `<option value="${d.id}">${esc(d.name)}</option>`).join('');
        document.getElementById('o-department').value = item?.departmentId || '';

        enhanceSearchableSelect(document.getElementById('o-department'),
            { placeholder: t('course.goTenPhongBan', 'Gõ tên hoặc mã phòng ban...') });

        openModal('modal-org');
    }

    async function save(e) {
        e.preventDefault();
        const kind = document.getElementById('o-kind').value;
        const id = document.getElementById('o-id').value;
        const departmentId = document.getElementById('o-department').value;

        const body = {
            name: document.getElementById('o-name').value.trim(),
            description: document.getElementById('o-description').value.trim() || null,
            isActive: document.getElementById('o-active').checked
        };
        if (kind === 'position') body.departmentId = departmentId ? +departmentId : null;
        if (kind === 'department') body.isHrDepartment = document.getElementById('o-hr').checked;

        try {
            if (id) await API.put(`/${kind}/${id}`, body); else await API.post(`/${kind}`, body);
            toastOk(t('organization.daLuu', 'Đã lưu.'));
            closeModal('modal-org');
            load();
        } catch (ex) { toastErr(ex.message); }
    }
})();
