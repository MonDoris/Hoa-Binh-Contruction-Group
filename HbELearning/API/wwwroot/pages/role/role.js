// File: wwwroot/pages/role/role.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    if (!Auth.require(['Admin'], 'role.view')) return;
    mountLayout('role', t('role.vaiTroPhanQuyen', 'Vai trò & Phân quyền'),
        t('role.dinhNghiaVaiTroVaChucNang', 'Định nghĩa vai trò và chức năng mỗi vai trò được sử dụng'));
    // Chỉ hiện nút thao tác mà vai trò thực sự được cấp — tránh bấm vào rồi nhận lỗi 403 từ API.
    const canCreate = Auth.can('role.create') ?? true;
    const canUpdate = Auth.can('role.update') ?? true;
    const canDelete = Auth.can('role.delete') ?? true;

    if (canCreate)
        setTopbarAction('<button class="btn" id="btn-new"><i class="ph ph-plus"></i> <span data-i18n="role.themVaiTro">Thêm vai trò</span></button>');

    let currentTab = 'system';
    let currentList = [];

    document.querySelectorAll('.tabs button').forEach(button => {
        button.addEventListener('click', () => {
            currentTab = button.dataset.tab;
            showTab(currentTab, 'system');
            // Không gọi lại API: cả hai tab đọc chung một danh sách vừa tải, chỉ khác phần lọc.
            renderTab();
        });
    });

    /** Chuyển sang tab "Vai trò tự tạo" — vai trò mới luôn là vai trò tự tạo, tạo xong mà vẫn đứng ở tab
     *  hệ thống thì người dùng không thấy thứ mình vừa thêm và tưởng lưu hỏng. */
    function goToCustomTab() {
        document.querySelector('.tabs button[data-tab="custom"]')?.click();
    }

    renderPermissionMatrix('r-function');
    bindPermissionMatrix('r-function');

    document.getElementById('btn-new')?.addEventListener('click', () => openForm());
    document.getElementById('role-form').addEventListener('submit', save);

    // Mở lại thẻ ghi trên địa chỉ trang (?tab=): tải lại trang, kể cả khi đổi ngôn ngữ, vẫn đứng nguyên thẻ đang xem.
    currentTab = tabFromUrl('system');
    showTab(currentTab, 'system');
    await load();

    /** Đếm số quyền đã cấp để hiện nhanh trên bảng, VD "5 chức năng · 14 quyền". */
    function permissionSummary(raw) {
        const keyList = parseFunctionKeys(raw) || [];
        if (!keyList.length) return `<span data-i18n="role.chuaCapQuyen" class="badge gray">Chưa cấp quyền</span>`;

        const moduleCount = new Set(keyList.map(x => x.split('.')[0])).size;
        return `<span class="small">${moduleCount} ${t('role.chucNang', 'chức năng')} · ${keyList.length} ${t('role.quyen', 'quyền')}</span>`;
    }

    /** Quyền hiển thị của một vai trò = quyền riêng CỘNG quyền nhận từ cha — đúng bằng thứ người dùng thực
     *  sự có sau khi đăng nhập (xem FunctionKey.Inherit ở server). Hiện mỗi quyền riêng thì con số trong
     *  bảng luôn nhỏ hơn thực tế, và Admin sẽ tưởng vai trò con đang thiếu quyền. */
    function mergeKeys(role) {
        const split = (raw) => String(raw || '').split(',').map(x => x.trim()).filter(Boolean);
        return [...new Set([...split(role.functionKeys), ...split(role.inheritedFunctionKeys)])].join(',');
    }

    /** Chỉ vai trò HỆ THỐNG được làm cha — ràng buộc một cấp, xem RoleController.ResolveParentAsync. */
    function fillParentSelect(role) {
        const select = document.getElementById('r-parent');
        const canPickParent = !role?.isSystem;

        select.innerHTML = `<option value="">${esc(t('role.khongKeThua', '— Không kế thừa —'))}</option>`
            + currentList.filter(x => x.isSystem && x.id !== role?.id)
                .map(x => `<option value="${x.id}">${esc(x.name)}</option>`).join('');
        select.value = role?.parentRoleId || '';
        select.disabled = !canPickParent;

        // Vai trò hệ thống không kế thừa ai: chúng CHÍNH LÀ gốc của cây, cho chọn cha chỉ tạo ra vòng lặp
        // và làm hỏng chuỗi duyệt vốn dựa đúng vào sáu cái tên đó.
        document.getElementById('r-parent-row').classList.toggle('hidden', !canPickParent);
    }

    async function load() {
        document.getElementById(`tab-${currentTab}`).innerHTML = skeletonHtml('list', { count: 5, avatar: false });
        try {
            currentList = await API.get('/role');
            renderTab();
        } catch (ex) {
            document.getElementById(`tab-${currentTab}`).innerHTML = `<div class="card"><div class="card-body">${esc(ex.message)}</div></div>`;
        }
    }

    /** Dựng bảng của tab đang mở từ danh sách đã tải. */
    function renderTab() {
        const isSystemTab = currentTab === 'system';
        const list = currentList.filter(r => !!r.isSystem === isSystemTab);
        const panel = document.getElementById(`tab-${currentTab}`);

        panel.innerHTML = `
            <div class="card">
                <div class="card-head">
                    <h3>${list.length} ${esc(t('role.vaiTro', 'vai trò'))}</h3>
                    <span class="small muted">${esc(isSystemTab
                        ? t('role.ghiChuVaiTroHeThong', 'Vai trò cài sẵn: chỉ sửa được quyền. Tên của chúng được dùng trong luồng duyệt khóa học nên không đổi và không xóa được.')
                        : t('role.ghiChuApDung', 'Đổi quyền xong, người dùng thuộc vai trò đó cần đăng xuất và đăng nhập lại để áp dụng.'))}</span>
                </div>
                <div class="table-wrap"><table>
                    <thead><tr>
                        <th data-i18n="role.tenVaiTro">Tên vai trò</th>
                        <th data-i18n="role.quyenDuocCap">Quyền được cấp</th>
                        <th class="text-center" data-i18n="role.soNguoiDung">Người dùng</th>
                        <th data-i18n="dashboard.colStatus">Trạng thái</th>
                        <th class="text-right"></th>
                    </tr></thead>
                    <tbody id="tbody"></tbody>
                </table></div>
            </div>`;

        const tbody = panel.querySelector('#tbody');
        const emptyText = isSystemTab
            ? t('common.empty', 'Chưa có dữ liệu.')
            : t('role.chuaCoVaiTroTuTao', 'Chưa có vai trò tự tạo nào. Bấm "+ Thêm vai trò" để tạo, và chọn một vai trò hệ thống làm vai trò cha để kế thừa quyền của nó.');

        tbody.innerHTML = list.length ? list.map(r => `
                <tr>
                    <!-- Không còn nhãn "Hệ thống" trên từng dòng: cả tab này đã là vai trò hệ thống rồi.
                         Cũng bỏ phần thụt đầu dòng của vai trò con — vai trò cha nằm ở tab bên kia nên thụt
                         vào chỉ trông như lỗi căn lề, dòng "Kế thừa từ ..." mới là thứ nói rõ nhánh nào. -->
                    <td><b>${esc(r.name)}</b>
                        ${r.parentRoleName
                            ? `<div class="small muted"><i class="ph ph-arrow-elbow-down-right"></i>
                                 ${esc(t('role.keThuaTu', 'Kế thừa từ'))} <b>${esc(r.parentRoleName)}</b></div>`
                            : (r.isSystem ? '' : `<div class="small muted">${esc(t('role.khongKeThuaAi', 'Không kế thừa vai trò nào'))}</div>`)}</td>
                    <td>${permissionSummary(mergeKeys(r))}${
                        r.parentRoleId ? `<div class="small muted">${esc(t('role.daGomQuyenCha', 'đã gồm quyền của vai trò cha'))}</div>` : ''}</td>
                    <td class="text-center">${r.userCount}</td>
                    <td>${r.isActive ? '<span data-i18n="organization.hoatDong" class="badge green">Hoạt động</span>' : '<span data-i18n="organization.ngung" class="badge gray">Ngừng</span>'}</td>
                    <td class="text-right nowrap">
                        ${canUpdate ? `<button data-i18n="role.phanQuyen" class="btn ghost sm" data-edit="${r.id}" style="background-color: yellow">Phân quyền</button>` : ''}
                        ${canDelete && !r.isSystem ? `<button data-i18n="common.delete" class="btn ghost sm" data-del="${r.id}" style="background-color: red; color: white">Xóa</button>` : ''}
                    </td>
                </tr>`).join('') : `<tr><td colspan="5">${emptyHtml(emptyText, '<i class="ph ph-shield-check"></i>')}</td></tr>`;

        tbody.querySelectorAll('[data-edit]').forEach(b =>
            b.addEventListener('click', () => openForm(currentList.find(x => x.id == b.dataset.edit))));

        tbody.querySelectorAll('[data-del]').forEach(b =>
            b.addEventListener('click', () => remove(currentList.find(x => x.id == b.dataset.del))));

        translateTree(panel);
    }

    async function remove(role) {
        if (!role) return;
        if (!await confirmDialog(
            t('role.xacNhanXoa', 'Xóa vai trò "{name}"?', { name: role.name }),
            { danger: true, okText: t('common.delete', 'Xóa') })) return;

        try {
            const result = await API.del(`/role/${role.id}`);
            toastOk(result.message);
            load();
        } catch (ex) { toastErr(ex.message); }
    }

    function openForm(role) {
        const isEdit = !!role;
        const isSystem = !!role?.isSystem;
        const isAdminRole = role?.name === 'Admin';

        document.getElementById('modal-title').textContent = isEdit
            ? t('role.phanQuyenChoVaiTro', 'Phân quyền: {name}', { name: role.name })
            : t('role.themVaiTro', 'Thêm vai trò');

        document.getElementById('r-id').value = role?.id || '';
        document.getElementById('r-name').value = role?.name || '';
        document.getElementById('r-name').disabled = isSystem;
        document.getElementById('r-name-hint').classList.toggle('hidden', !isSystem);
        document.getElementById('r-description').value = role?.description || '';
        document.getElementById('r-active').checked = role ? role.isActive : true;
        document.getElementById('r-active').disabled = isSystem;
        document.getElementById('r-admin-hint').classList.toggle('hidden', !isAdminRole);

        fillParentSelect(role);
        // Bảng tick hiện quyền RIÊNG của vai trò, không gộp quyền cha vào: gộp rồi lưu lại là quyền cha bị
        // sao chép cứng vào con, sau này Admin sửa quyền cha thì con không nhận được thay đổi nữa.
        setPermissionMatrix('r-function', role?.functionKeys, isAdminRole);
        openModal('modal-role');
    }

    async function save(e) {
        e.preventDefault();
        const id = document.getElementById('r-id').value;

        const body = {
            name: document.getElementById('r-name').value.trim(),
            description: document.getElementById('r-description').value.trim() || null,
            functionKeys: getPermissionMatrix('r-function'),
            isActive: document.getElementById('r-active').checked,
            parentRoleId: +document.getElementById('r-parent').value || null
        };

        try {
            if (id) {
                const result = await API.put(`/role/${id}`, body);
                toastOk(result.message);
            } else {
                await API.post('/role', body);
                toastOk(t('role.daLuuVaiTro', 'Đã lưu vai trò.'));
                // Vai trò mới luôn là vai trò tự tạo — chuyển sang đúng tab chứa nó, nếu không người dùng
                // bấm Lưu xong vẫn thấy y nguyên sáu dòng cũ và tưởng không lưu được.
                goToCustomTab();
            }
            closeModal('modal-role');
            load();
        } catch (ex) { toastErr(ex.message); }
    }
})();
