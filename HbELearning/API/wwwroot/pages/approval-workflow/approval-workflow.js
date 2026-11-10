// File: wwwroot/pages/approval-workflow/approval-workflow.js
(async function () {
    await Lang.ready;

    if (!Auth.require(['Admin', 'Trưởng Nhóm', 'Trưởng phòng', 'Giám Đốc'], 'approval-workflow.view')) return;
    
    mountLayout('approval-workflow', t('menu.approval-workflow', 'Luồng cấp duyệt'),
        t('approval-workflow.subtitle', 'Quản lý các quy trình phê duyệt nội dung khóa học'));

    const isAdmin = Auth.isAdmin;
    const canViewCompany = isAdmin || Auth.hasRole('Trưởng phòng') || Auth.hasRole('Giám Đốc');
    const myDepartmentId = Auth.profile?.departmentId ?? null;
    const myDepartmentName = Auth.profile?.departmentName || 'phòng ban của bạn';

    const canCreate = Auth.can('approval-workflow.create') ?? true;
    const canUpdate = Auth.can('approval-workflow.update') ?? true;
    const canDelete = Auth.can('approval-workflow.delete') ?? true;

    let currentTab = canViewCompany && !isAdmin && qs('tab') === 'company' ? 'company' : 'department';
    let currentStatusFilter = 'all';
    let cachedList = [];

    const listContainer = document.getElementById('workflow-list');
    const tabBox = document.getElementById('aw-tabs');
    tabBox.querySelectorAll('.aw-tab').forEach(x => x.classList.toggle('active', x.dataset.tab === currentTab));
    const filterWrap = document.getElementById('aw-filter-wrap');
    const filterSelect = document.getElementById('aw-filter-department');
    const quickSearch = document.getElementById('aw-quick-search');
    const searchClearBtn = document.getElementById('aw-search-clear');
    const statusFilterBox = document.getElementById('aw-status-filter');
    const scopeNote = document.getElementById('scopeNote');
    const summaryCount = document.getElementById('aw-summary-count');

    const [roleList, departmentList] = await Promise.all([
        API.get('/approvalworkflow/role-option'),
        API.get('/department')
    ]);
    const activeRoleList = roleList || [];
    const roleMap = new Map(activeRoleList.map(r => [r.id, roleLabel(r.name)]));
    const deptMap = new Map((departmentList || []).map(d => [d.id, d.name]));

    buildFilterOption();
    syncScopeUi();

    tabBox.querySelectorAll('.aw-tab').forEach(btn =>
        btn.addEventListener('click', () => switchTab(btn.dataset.tab)));

    statusFilterBox.querySelectorAll('.aw-status-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            currentStatusFilter = btn.dataset.status;
            statusFilterBox.querySelectorAll('.aw-status-btn').forEach(x => x.classList.toggle('active', x === btn));
            renderList();
        });
    });

    function switchTab(tab) {
        if (currentTab === tab) return;
        currentTab = tab;
        tabBox.querySelectorAll('.aw-tab').forEach(x => x.classList.toggle('active', x.dataset.tab === tab));
        rememberTab(tab, 'department');
        buildFilterOption();
        syncScopeUi();
        load();
    }

    filterSelect.addEventListener('change', load);
    quickSearch.addEventListener('input', () => {
        searchClearBtn.style.display = quickSearch.value ? 'inline-flex' : 'none';
        renderList();
    });
    searchClearBtn.addEventListener('click', () => {
        quickSearch.value = '';
        searchClearBtn.style.display = 'none';
        quickSearch.focus();
        renderList();
    });

    document.body.addEventListener('click', (e) => {
        if (e.target.closest('#btn-new') || e.target.closest('#btn-empty-new')) openForm();
        if (e.target.closest('#btn-empty-reset')) {
            quickSearch.value = '';
            searchClearBtn.style.display = 'none';
            currentStatusFilter = 'all';
            statusFilterBox.querySelectorAll('.aw-status-btn').forEach(x => x.classList.toggle('active', x.dataset.status === 'all'));
            renderList();
        }
    });
    document.getElementById('btn-add-step').addEventListener('click', () => addStepRow());
    document.getElementById('workflow-form').addEventListener('submit', save);
    document.getElementById('clone-form').addEventListener('submit', saveClone);

    await load();

    function showDepartmentBadge() { return isAdmin || currentTab === 'company'; }
    function isCompanyTab() { return currentTab === 'company'; }

    function buildFilterOption() {
        const exceptId = (!isAdmin && isCompanyTab()) ? myDepartmentId : null;
        filterSelect.innerHTML = '<option value="">Tất cả phòng ban</option>'
            + '<option value="shared">Dùng chung (Global)</option>'
            + departmentList.filter(d => d.id !== exceptId)
                .map(d => `<option value="${d.id}">${esc(d.name)} (${esc(d.code)})</option>`).join('');
        filterSelect.value = '';
        enhanceSearchableSelect(filterSelect, { placeholder: 'Tìm phòng ban...' });
    }

    function syncScopeUi() {
        const showTab = canViewCompany && !isAdmin;
        const showFilter = isAdmin || isCompanyTab();
        tabBox.style.display = showTab ? '' : 'none';
        filterWrap.style.display = showFilter ? '' : 'none';

        setTopbarAction(canCreate && !isCompanyTab()
            ? `<button class="btn" id="btn-new"><i class="ph ph-plus"></i> <span data-i18n="approval-workflow.taoLuongDuyet">Tạo luồng duyệt</span></button>`
            : '');

        scopeNote.textContent = isAdmin
            ? 'Quản lý toàn bộ luồng duyệt của hệ thống. Có thể lọc theo từng phòng ban hoặc tìm trực tiếp.'
            : isCompanyTab()
                ? `Danh sách luồng từ các phòng ban khác. Bạn có thể nhân bản về ${myDepartmentName}.`
                : `Cấu hình định tuyến phê duyệt nội dung của ${myDepartmentName}.`;
    }

    async function load() {
        listContainer.innerHTML = Array(6).fill(`
            <div class="wf-card" style="min-height: 176px;" aria-hidden="true">
                <div class="skel skel-title skel-w-60"></div>
                <div class="skel skel-line skel-w-90"></div>
                <div class="skel skel-btn skel-w-full" style="height: 44px; border-radius: 8px;"></div>
                <div class="skel skel-line sm skel-w-40" style="margin-top: auto;"></div>
            </div>`).join('');

        const query = new URLSearchParams();
        if (isAdmin || isCompanyTab()) {
            const filter = filterSelect.value;
            if (filter === 'shared') query.set('sharedOnly', 'true');
            else if (filter) query.set('departmentId', filter);
        }
        const url = (isCompanyTab() ? '/approvalworkflow/company' : '/approvalworkflow')
            + (query.toString() ? `?${query}` : '');

        try {
            cachedList = await API.get(url) || [];
            const activeCount = cachedList.filter(x => x.isActive).length;
            const defaultCount = cachedList.filter(x => x.isDefault).length;
            summaryCount.innerHTML = cachedList.length
                ? `<span class="aw-stat-pill">Tổng <b>${cachedList.length}</b> luồng</span>
                   <span class="aw-stat-pill is-active-pill"><b>${activeCount}</b> đang bật</span>
                   ${defaultCount ? `<span class="aw-stat-pill"><b>${defaultCount}</b> mặc định</span>` : ''}`
                : '';
            renderList();
        } catch (ex) {
            listContainer.innerHTML = `<div style="grid-column: 1/-1; color: var(--aw-danger); padding: 16px;">Lỗi tải dữ liệu: ${esc(ex.message)}</div>`;
        }
    }

    function renderList() {
        const kw = (quickSearch.value || '').trim().toLowerCase();
        const list = cachedList.filter(w => {
            if (currentStatusFilter === 'active' && !w.isActive) return false;
            if (currentStatusFilter === 'inactive' && w.isActive) return false;
            if (!kw) return true;
            return (w.name || '').toLowerCase().includes(kw) ||
                (w.description || '').toLowerCase().includes(kw) ||
                (w.departmentName || '').toLowerCase().includes(kw) ||
                (w.stepList || []).some(s =>
                    (s.name || '').toLowerCase().includes(kw) ||
                    (s.roleName || roleMap.get(s.roleId) || '').toLowerCase().includes(kw)
                );
        });

        if (!list.length) {
            const isFiltered = kw || currentStatusFilter !== 'all';
            listContainer.innerHTML = `
                <div class="aw-empty-state">
                    <div class="aw-empty-icon"><i class="ph ${isFiltered ? 'ph-magnifying-glass' : 'ph-git-merge'}"></i></div>
                    <h4 class="aw-empty-title">${isFiltered ? 'Không có kết quả phù hợp' : 'Chưa có luồng cấp duyệt nào'}</h4>
                    <p class="aw-empty-sub">${isFiltered
                        ? 'Hãy thử tìm bằng từ khóa khác hoặc đặt lại bộ lọc để xem toàn bộ danh sách.'
                        : 'Thiết lập trình tự các trạm phê duyệt để chuẩn hóa quy trình xuất bản khóa học.'}</p>
                    ${isFiltered
                        ? `<button type="button" class="btn-ghost" id="btn-empty-reset" style="margin-top:6px;"><i class="ph ph-arrow-counter-clockwise"></i> Đặt lại bộ lọc</button>`
                        : (canCreate && !isCompanyTab()
                            ? `<button type="button" class="btn-primary" id="btn-empty-new" style="margin-top:6px;"><i class="ph ph-plus"></i> Tạo luồng duyệt đầu tiên</button>`
                            : '')}
                </div>`;
            return;
        }

        listContainer.innerHTML = list.map(w => renderCard(w)).join('');
        bindCardActions(list);
    }

    function renderStepPipeline(stepList) {
        if (!stepList || !stepList.length) {
            return '<span style="color: var(--aw-text-muted); font-size: 0.8rem;"><i class="ph ph-warning-circle"></i> Chưa cấu hình trạm duyệt</span>';
        }
        return stepList.map((s, i) => {
            const title = s.name || ('Bước ' + (i + 1));
            const roleTxt = s.roleName || roleMap.get(s.roleId) || '';
            const crossDeptTxt = s.departmentName || (s.departmentId ? deptMap.get(s.departmentId) : '');
            const subParts = [];
            if (roleTxt) subParts.push(esc(roleTxt));
            if (crossDeptTxt) subParts.push(`<i class="ph ph-arrow-bend-up-right"></i> ${esc(crossDeptTxt)}`);
            if (s.allowDelegate) subParts.push(`<i class="ph ph-user-switch" title="Cho phép ủy quyền"></i>`);

            return `${i > 0 ? '<span class="wf-step-sep"><i class="ph ph-arrow-right"></i></span>' : ''}
                <div class="wf-step-pill">
                    <span class="wf-step-num">${i + 1}</span>
                    <div class="wf-step-text">
                        <span class="wf-step-name">${esc(title)}</span>
                        ${subParts.length ? `<span class="wf-step-sub">${subParts.join(' · ')}</span>` : ''}
                    </div>
                </div>`;
        }).join('');
    }

    function renderCard(w) {
        const stepHtml = renderStepPipeline(w.stepList);

        const deptBadge = (showDepartmentBadge() || !w.departmentId)
            ? (w.departmentName
                ? `<span class="wf-tag"><i class="ph ph-buildings"></i> ${esc(w.departmentName)}</span>`
                : `<span class="wf-tag tag-global"><i class="ph ph-globe"></i> Global</span>`)
            : '';

        return `
        <div class="wf-card ${w.isActive ? '' : 'is-inactive'} ${w.isDefault ? 'is-default' : ''}">
            <div class="wf-card-top">
                <div class="wf-head-info">
                    <div class="wf-title-row">
                        <span class="wf-status-dot ${w.isActive ? 'on' : 'off'}" title="${w.isActive ? 'Đang hoạt động' : 'Tạm ngưng'}"></span>
                        <span class="wf-title">${esc(w.name)}</span>
                    </div>
                    ${w.description ? `<div class="wf-desc">${esc(w.description)}</div>` : ''}
                </div>
                <div class="wf-actions">${actionHtml(w)}</div>
            </div>
            
            <div class="wf-steps-preview" title="Trình tự phê duyệt (${w.stepList?.length || 0} bước)">
                ${stepHtml}
            </div>

            <div class="wf-meta">
                <div class="wf-meta-left">
                    ${w.isDefault ? '<span class="wf-tag tag-default"><i class="ph ph-star-four"></i> Mặc định</span>' : ''}
                    ${!w.isActive ? '<span class="wf-tag"><i class="ph ph-pause-circle"></i> Tạm ngưng</span>' : ''}
                    ${deptBadge}
                </div>
                <div class="wf-meta-right">
                    <span class="wf-meta-stat" title="Số bước duyệt"><i class="ph ph-git-commit"></i> ${w.stepList?.length || 0} bước</span>
                    <span>·</span>
                    <span class="wf-meta-stat wf-course-count" title="Số khóa học đang áp dụng"><i class="ph ph-books"></i> ${w.courseCount} khóa học</span>
                </div>
            </div>
        </div>`;
    }

    function actionHtml(w) {
        if (isCompanyTab()) {
            return canViewCompany ? `<button class="btn-icon" data-clone="${w.id}" title="Nhân bản về ${esc(myDepartmentName)}"><i class="ph ph-copy"></i></button>` : '';
        }
        return (isAdmin && canCreate ? `<button class="btn-icon" data-clone="${w.id}" title="Nhân bản"><i class="ph ph-copy"></i></button>` : '')
            + (canUpdate ? `<button class="btn-icon" data-edit="${w.id}" title="Chỉnh sửa"><i class="ph ph-pencil-simple"></i></button>` : '')
            + (canDelete ? `<button class="btn-icon del" data-del="${w.id}" title="Xóa"><i class="ph ph-trash"></i></button>` : '');
    }

    function bindCardActions(list) {
        listContainer.querySelectorAll('[data-edit]').forEach(b => b.addEventListener('click', () => openForm(list.find(x => x.id == b.dataset.edit))));
        listContainer.querySelectorAll('[data-clone]').forEach(b => b.addEventListener('click', () => openCloneForm(list.find(x => x.id == b.dataset.clone))));
        listContainer.querySelectorAll('[data-del]').forEach(b => b.addEventListener('click', async () => {
            const item = list.find(x => x.id == b.dataset.del);
            if (!await confirmDialog(`Xóa luồng cấp duyệt "${item?.name || ''}"?`, { danger: true, okText: 'Xóa' })) return;
            try { const r = await API.del(`/approvalworkflow/${b.dataset.del}`); toastOk(r.message); load(); } catch (ex) { toastErr(ex.message); }
        }));
    }

    let cloneSourceId = null;
    function openCloneForm(workflow) {
        if (!workflow) return;
        cloneSourceId = workflow.id;
        document.getElementById('clone-source').innerHTML = `Luồng gốc: <b>${esc(workflow.name)}</b> · <span style="color:var(--aw-text-muted);">${esc(workflow.departmentName || 'Global')}</span>`;
        document.getElementById('c-name').value = '';
        document.getElementById('clone-department-field').style.display = isAdmin ? '' : 'none';
        document.getElementById('clone-target-fixed').style.display = isAdmin ? 'none' : '';
        document.getElementById('clone-target-name').textContent = myDepartmentName;

        const select = document.getElementById('c-department');
        select.innerHTML = (workflow.departmentId ? '<option value="">— Global (Dùng chung) —</option>' : '')
            + departmentList.filter(d => d.id !== workflow.departmentId)
                .map(d => `<option value="${d.id}">${esc(d.name)}</option>`).join('');
        select.value = '';
        enhanceSearchableSelect(select, { placeholder: 'Tìm phòng ban...' });
        openModal('modal-clone');
    }

    async function saveClone(e) {
        e.preventDefault();
        const departmentId = isAdmin ? (+document.getElementById('c-department').value || null) : myDepartmentId;
        try {
            const r = await API.post(`/approvalworkflow/${cloneSourceId}/clone`, { departmentId, name: document.getElementById('c-name').value.trim() || null });
            toastOk(r.message);
            closeModal('modal-clone');
            if (isCompanyTab()) switchTab('department'); else await load();
        } catch (ex) { toastErr(ex.message); }
    }

    function departmentOptionHtml(selectedId, blankLabel) {
        return `<option value="">${esc(blankLabel)}</option>` + departmentList.map(d => `<option value="${d.id}"${d.id === selectedId ? ' selected' : ''}>${esc(d.name)}</option>`).join('');
    }

    function openForm(workflow) {
        document.getElementById('modal-title').textContent = workflow ? 'Chỉnh sửa luồng duyệt' : 'Tạo luồng duyệt mới';
        document.getElementById('f-id').value = workflow?.id || '';
        document.getElementById('f-name').value = workflow?.name || '';
        document.getElementById('f-description').value = workflow?.description || '';

        const departmentSelect = document.getElementById('f-workflow-department');
        const formDepartmentId = isAdmin ? (workflow?.departmentId || '') : (workflow?.departmentId || myDepartmentId || '');
        departmentSelect.innerHTML = departmentOptionHtml(formDepartmentId || null, '— Dùng chung (Global) —');
        departmentSelect.value = formDepartmentId;
        enhanceSearchableSelect(departmentSelect);

        document.getElementById('f-department-group').style.display = isAdmin ? '' : 'none';
        document.getElementById('f-department-fixed').style.display = isAdmin ? 'none' : '';
        document.getElementById('f-department-fixed-name').textContent = myDepartmentName;
        
        if (!departmentSelect._boundStepSync) {
            departmentSelect._boundStepSync = true;
            departmentSelect.addEventListener('change', () => document.querySelectorAll('#step-list .step-node').forEach(row => row._syncApproverMode?.()));
        }
        document.getElementById('f-active').checked = workflow ? workflow.isActive : true;
        document.getElementById('f-default').checked = !!workflow?.isDefault;

        document.getElementById('step-list').innerHTML = '';
        (workflow?.stepList?.length ? workflow.stepList : [null]).forEach(step => addStepRow(step));
        
        openModal('modal-workflow');
    }

    function addStepRow(step) {
        const box = document.getElementById('step-list');
        const row = document.createElement('div');
        row.className = 'step-node';
        row.innerHTML = `
            <div class="step-marker"></div>
            <div class="step-node-top">
                <span class="step-node-title">Trạm duyệt</span>
                <div class="step-actions-bar">
                    <button type="button" class="btn-step-act move-up" title="Chuyển lên"><i class="ph ph-caret-up"></i></button>
                    <button type="button" class="btn-step-act move-down" title="Chuyển xuống"><i class="ph ph-caret-down"></i></button>
                    <button type="button" class="btn-step-act danger del" title="Xóa bước này"><i class="ph ph-trash"></i></button>
                </div>
            </div>
            
            <div class="step-grid">
                <div class="fg" style="margin: 0;">
                    <label>Tên bước (VD: Trưởng nhóm duyệt)</label>
                    <input type="text" class="clean-input s-name" maxlength="100" value="${esc(step?.name || '')}" placeholder="Nhập tên trạm...">
                </div>
                <div class="fg" style="margin: 0;">
                    <label>Vai trò duyệt <span style="color: var(--aw-danger);">*</span></label>
                    <select class="clean-input s-role">
                        <option value="">— Chọn vai trò —</option>
                        ${activeRoleList.map(r => `<option value="${r.id}">${esc(roleLabel(r.name))}</option>`).join('')}
                    </select>
                </div>
            </div>

            <div class="step-grid" style="margin-top: 10px;">
                <div class="fg" style="margin: 0;">
                    <label>Cơ chế phân công</label>
                    <select class="clean-input s-approver-mode">
                        <option value="0">Bất kỳ ai có vai trò này (Any)</option>
                        <option value="2">Chỉ định cố định 1 người</option>
                    </select>
                </div>
                <div class="fg s-fixed-user-wrap" style="margin: 0; display: none;">
                    <label>Người duyệt cố định <span style="color: var(--aw-danger);">*</span></label>
                    <select class="clean-input s-fixed-user"><option value="">— Chọn vai trò trước —</option></select>
                </div>
            </div>

            <div class="step-options-row">
                <label class="check-inline">
                    <input type="checkbox" class="s-cross-dept">
                    <span>Chuyển qua phòng ban khác duyệt</span>
                </label>

                <label class="check-inline">
                    <input type="checkbox" class="s-delegate" ${step?.allowDelegate ? 'checked' : ''}>
                    <span>Cho phép ủy quyền (Delegate)</span>
                </label>
            </div>

            <div class="fg s-department-wrap" style="display: none;">
                <div class="s-department-label"><i class="ph ph-arrow-bend-down-right"></i> Phòng ban tiếp nhận phê duyệt ở bước này:</div>
                <select class="clean-input s-department">
                    <option value="">— Chọn phòng ban tiếp nhận duyệt —</option>
                    ${departmentList.map(d => `<option value="${d.id}">${esc(d.name)}</option>`).join('')}
                </select>
            </div>
        `;

        row.querySelector('.s-role').value = step?.roleId || '';
        const stepDepartment = row.querySelector('.s-department');
        const stepDepartmentWrap = row.querySelector('.s-department-wrap');
        const crossDepartment = row.querySelector('.s-cross-dept');

        stepDepartment.value = step?.departmentId || '';
        crossDepartment.checked = !!step?.departmentId;
        stepDepartmentWrap.style.display = crossDepartment.checked ? 'block' : 'none';

        crossDepartment.addEventListener('change', () => {
            stepDepartmentWrap.style.display = crossDepartment.checked ? 'block' : 'none';
            if (!crossDepartment.checked) { stepDepartment.value = ''; enhanceSearchableSelect(stepDepartment); }
            row._syncApproverMode?.();
        });

        const APPROVER_MODE = { AnyOfRole: 0, PickOne: 1, FixedUser: 2 };
        const stepMode = APPROVER_MODE[step?.approverMode] ?? 0;
        row.querySelector('.s-approver-mode').value = stepMode === APPROVER_MODE.PickOne ? 0 : stepMode;
        bindApproverMode(row, step?.fixedUserId || '');

        row.querySelector('.del').addEventListener('click', () => { 
            row.remove();
            renumberStep();
        });
        
        row.querySelector('.move-up').addEventListener('click', () => {
            const prev = row.previousElementSibling; if (prev) { box.insertBefore(row, prev); renumberStep(); }
        });
        row.querySelector('.move-down').addEventListener('click', () => {
            const next = row.nextElementSibling; if (next) { box.insertBefore(next, row); renumberStep(); }
        });

        box.appendChild(row);
        enhanceSearchableSelect(row.querySelector('.s-role'));
        enhanceSearchableSelect(row.querySelector('.s-department'));
        renumberStep();
    }

    const userListCache = new Map();
    async function userListOfRole(roleId, departmentId) {
        if (!roleId) return [];
        const key = `${roleId}|${departmentId || ''}`;
        if (!userListCache.has(key)) {
            const query = `/approvalworkflow/approver-option?roleId=${roleId}` + (departmentId ? `&departmentId=${departmentId}` : '');
            userListCache.set(key, await API.get(query) || []);
        }
        return userListCache.get(key);
    }

    function effectiveDepartmentIdOf(row) {
        return +row.querySelector('.s-department').value || +document.getElementById('f-workflow-department').value || 0;
    }

    function bindApproverMode(row, selectedUserId) {
        const modeSelect = row.querySelector('.s-approver-mode');
        const roleSelect = row.querySelector('.s-role');
        const wrap = row.querySelector('.s-fixed-user-wrap');
        const userSelect = row.querySelector('.s-fixed-user');

        const sync = async (keepSelected) => {
            const mode = +modeSelect.value;
            wrap.style.display = mode === 2 ? 'block' : 'none';
            if (mode !== 2) return;

            const departmentId = effectiveDepartmentIdOf(row);
            const wanted = keepSelected ? userSelect.value : '';
            const userList = await userListOfRole(roleSelect.value, departmentId);

            if (!roleSelect.value) {
                userSelect.innerHTML = '<option value="">— Chọn vai trò trước —</option>';
            } else if (!userList.length) {
                userSelect.innerHTML = '<option value="">— Không có nhân sự phù hợp —</option>';
            } else {
                userSelect.innerHTML = '<option value="">— Chọn nhân sự —</option>' +
                    userList.map(u => `<option value="${u.id}">${esc(u.fullName)}${u.departmentName ? ' · ' + esc(u.departmentName) : ''}</option>`).join('');
            }
            userSelect.value = wanted || '';
            enhanceSearchableSelect(userSelect, { placeholder: 'Gõ tên nhân sự...' });
        };

        modeSelect.addEventListener('change', () => sync(false));
        roleSelect.addEventListener('change', () => sync(false));
        row.querySelector('.s-department').addEventListener('change', () => sync(false));
        row._syncApproverMode = () => sync(true);

        userSelect.value = selectedUserId || '';
        sync(true);
    }

    function renumberStep() {
        const rows = document.querySelectorAll('#step-list .step-node');
        rows.forEach((row, i) => { 
            row.querySelector('.step-marker').textContent = i + 1;
            row.querySelector('.step-node-title').textContent = `Bước ${i + 1}`;
            const upBtn = row.querySelector('.move-up');
            const downBtn = row.querySelector('.move-down');
            if (upBtn) upBtn.disabled = i === 0;
            if (downBtn) downBtn.disabled = i === rows.length - 1;
        });
        const countLabel = document.getElementById('step-count-label');
        if (countLabel) countLabel.textContent = `${rows.length} bước`;
    }

    async function askWorkflowScope() {
        const departmentSelect = document.getElementById('f-workflow-department');
        const selectedId = isAdmin ? (+departmentSelect.value || null) : myDepartmentId;
        const selectedName = isAdmin
            ? (departmentList.find(d => d.id === selectedId)?.name || null)
            : myDepartmentName;

        const answer = await confirmDialog(
            'Bạn có muốn áp dụng luồng cấp duyệt này cho toàn công ty không? '
            + 'Chọn "Có" thì mọi phòng ban đều thấy và chọn được luồng này cho khóa học của mình. '
            + (selectedName
                ? `Chọn "Không" thì chỉ phòng ban ${selectedName} dùng được.`
                : 'Chọn "Không" thì bạn cần chọn "Phòng ban sở hữu" trước khi lưu.'),
            { title: 'Phạm vi áp dụng', okText: 'Có, toàn công ty', cancelText: 'Không' });

        if (answer === null) return undefined;
        if (answer) return null;

        if (!selectedId) {
            toastErr('Hãy chọn "Phòng ban sở hữu" cho luồng rồi bấm Lưu lại.');
            return undefined;
        }
        return selectedId;
    }

    async function save(e) {
        e.preventDefault();
        const rowList = [...document.querySelectorAll('#step-list .step-node')];
        if (!rowList.length) { toastErr('Vui lòng tạo ít nhất một Trạm Duyệt.'); return; }
        if (rowList.some(row => !row.querySelector('.s-role').value)) { toastErr('Vui lòng chỉ định vai trò cho tất cả các trạm.'); return; }
        if (rowList.some(row => row.querySelector('.s-cross-dept').checked && !row.querySelector('.s-department').value)) {
            toastErr('Trạm đang bật "Chuyển qua phòng ban khác duyệt" nhưng chưa chọn phòng ban.'); return;
        }
        if (rowList.some(row => +row.querySelector('.s-approver-mode').value === 2 && !row.querySelector('.s-fixed-user').value)) {
            toastErr('Trạm đang đặt "Chỉ định cố định 1 người" nhưng chưa chọn nhân sự.'); return;
        }

        if (!document.getElementById('f-name').value.trim()) { toastErr('Vui lòng nhập tên luồng cấp duyệt.'); return; }

        const departmentId = await askWorkflowScope();
        if (departmentId === undefined) return;

        const body = {
            name: document.getElementById('f-name').value.trim(),
            description: document.getElementById('f-description').value.trim() || null,
            departmentId,
            isActive: document.getElementById('f-active').checked,
            isDefault: document.getElementById('f-default').checked,
            stepList: rowList.map(row => ({
                name: row.querySelector('.s-name').value.trim() || null,
                roleId: +row.querySelector('.s-role').value,
                departmentId: +row.querySelector('.s-department').value || null,
                allowDelegate: row.querySelector('.s-delegate').checked,
                approverMode: +row.querySelector('.s-approver-mode').value,
                fixedUserId: +row.querySelector('.s-fixed-user').value || null
            }))
        };

        const id = document.getElementById('f-id').value;
        const btnSave = e.target.querySelector('button[type="submit"]');
        const originalText = btnSave.innerHTML;
        btnSave.textContent = 'Đang lưu...';
        btnSave.disabled = true;

        try {
            const r = id ? await API.put(`/approvalworkflow/${id}`, body) : await API.post('/approvalworkflow', body);
            toastOk(r.message);
            closeModal('modal-workflow');
            load();
        } catch (ex) { toastErr(ex.message); } finally {
            btnSave.innerHTML = originalText;
            btnSave.disabled = false;
        }
    }
})();
