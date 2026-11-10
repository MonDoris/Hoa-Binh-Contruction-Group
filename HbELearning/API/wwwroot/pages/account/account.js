// File: wwwroot/pages/account/account.js
(async function () {
    await Lang.ready;
    if (!Auth.require()) return;
    mountLayout('information', t('account.taiKhoanCuaToi', 'Tài khoản của tôi'), t('account.xemVaCapNhatThongTinCaNhanCuaB', 'Xem và cập nhật thông tin cá nhân của bạn'));

    let profile = null;
    ['p-current', 'p-new', 'p-confirm'].forEach(id => attachPasswordEye(document.getElementById(id)));
    attachPasswordStrength(document.getElementById('p-new'));

    document.getElementById('btn-edit').addEventListener('click', openEdit);
    document.getElementById('btn-password').addEventListener('click', () => {
        document.getElementById('password-form').reset();
        document.getElementById('p-new').dispatchEvent(new Event('input'));
        openModal('modal-password');
    });
    
    document.getElementById('edit-form').addEventListener('submit', saveProfile);
    document.getElementById('password-form').addEventListener('submit', savePassword);
    document.getElementById('avatar-input').addEventListener('change', uploadAvatar);
    // Gắn MỘT lần ở đây. Trước đây gắn trong renderProfile() — hàm chạy lại sau mỗi lần lưu hồ sơ, nên lưu vài
    // lần là một lần chọn ảnh bìa bị tải lên vài lần.
    document.getElementById('cover-input').addEventListener('change', uploadCover);

    await load();
    loadCourseAndCertificate();

    /** Tải ảnh bìa lên rồi GHI vào hồ sơ — chỉ tải lên thôi thì tệp nằm trên máy chủ nhưng hồ sơ không trỏ tới,
     *  tải lại trang là ảnh bìa biến mất. Ảnh hiện ngay từ máy người dùng trong lúc chờ; lỗi thì trả về ảnh cũ. */
    async function uploadCover(e) {
        const file = e.target.files[0];
        if (!file) return;

        const localPreviewUrl = URL.createObjectURL(file);
        renderCover(localPreviewUrl);
        try {
            const uploaded = await API.upload(file, 'cover');
            profile = await API.put('/auth/me/cover', { coverUrl: uploaded.url });
            renderProfile();
            toastOk(t('account.daCapNhatAnhBia', 'Đã cập nhật ảnh bìa.'));
        } catch (ex) {
            renderCover(profile?.coverUrl);
            toastErr(ex.message);
        } finally {
            URL.revokeObjectURL(localPreviewUrl);
            e.target.value = '';
        }
    }

    /** Vẽ ảnh bìa; không có ảnh thì về dải màu mặc định trong CSS của .profile-cover. */
    function renderCover(url) {
        const coverEl = document.getElementById('acc-cover');
        if (!url) { coverEl.style.removeProperty('background'); return; }
        // encodeURI + thay dấu nháy: url đi thẳng vào CSS, không để ký tự nào phá vỡ chuỗi url('...').
        coverEl.style.background = `center / cover no-repeat url('${encodeURI(url).replace(/'/g, '%27')}')`;
    }

    async function load() {
        try {
            profile = await API.get('/auth/me');
            renderProfile();
        } catch (ex) { toastErr(ex.message); }
    }

    function renderProfile() {
        document.getElementById('acc-avatar').outerHTML = profile.avatarUrl
            ? `<img class="avatar lg" id="acc-avatar" src="${esc(profile.avatarUrl)}" alt="">`
            : `<div class="avatar lg" id="acc-avatar">${esc(initial(profile.fullName))}</div>`;
            
        document.getElementById('acc-name').textContent = profile.fullName;
        document.getElementById('acc-role').textContent = profile.roleName;
        document.getElementById('acc-code').textContent = profile.employeeCode;
        document.getElementById('acc-email').textContent = profile.email;
        document.getElementById('acc-username').textContent = profile.userName || '—';
        document.getElementById('acc-username-row').hidden = !profile.userName;

        document.getElementById('d-phone').textContent = profile.phoneNumber || '—';
        document.getElementById('d-department').textContent = profile.departmentName || '—';
        document.getElementById('d-position').textContent = profile.positionName || '—';
        document.getElementById('d-created').textContent = formatDate(profile.createdAt);
        document.getElementById('d-last-login').textContent = profile.lastLoginAt ? formatDate(profile.lastLoginAt, true) : t('account.chuaDangNhapLanNaoKhac', 'Chưa đăng nhập lần nào khác');
        renderCover(profile.coverUrl);
        Auth.save(Auth.token, { ...Auth.profile, ...profile });
    }

    async function loadCourseAndCertificate() {
        const courseTbody = document.getElementById('course-tbody');
        const certBox = document.getElementById('cert-box');
        
        courseTbody.innerHTML = skeletonHtml('table', { rows: 4, cols: 4 });
        certBox.innerHTML = skeletonHtml('cards', { count: 4, thumb: false });

        try {
            const [enrollmentList, certificateList] = await Promise.all([
                API.get('/enrollment/my'),
                API.get('/certificate/my')
            ]);
            renderStat(enrollmentList, certificateList);
            renderCourse(enrollmentList);
            renderCertificate(certificateList);
        } catch (ex) {
            courseTbody.innerHTML = `<tr><td colspan="4">${emptyHtml(ex.message, '<i class="ph ph-warning"></i>')}</td></tr>`;
            certBox.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        }
    }

    function renderStat(enrollmentList, certificateList) {
        const stat = [
            { label: t('dashboard.assignedCourse', 'Khóa học được giao'), value: enrollmentList.length, css: '' },
            { label: t('dashboard.inProgress', 'Đang học'), value: enrollmentList.filter(x => x.status === 'InProgress').length, css: 'accent' },
            { label: t('index.stat-enroll', 'Hoàn thành'), value: enrollmentList.filter(x => x.status === 'Completed').length, css: 'ok' },
            { label: t('index.stat-cert', 'Chứng nhận'), value: certificateList.length, css: '' }
        ];
        
        document.getElementById('stat-box').innerHTML = stat.map(s => `
            <div class="stat ${s.css}">
                <div class="label">${s.label}</div>
                <div class="value">${s.value}</div>
            </div>
        `).join('');
    }

    function renderCourse(enrollmentList) {
        const tbody = document.getElementById('course-tbody');
        const recent = enrollmentList.slice(0, 5);
        
        tbody.innerHTML = recent.length ? recent.map(x => `
            <tr>
                <td>
                    <b>${esc(x.courseTitle)}</b>
                    <div class="small muted">${esc(x.courseCode)}</div>
                </td>
                <td>${statusBadge(x.status)}</td>
                <td>${progressBar(x.progressPercent)}</td>
                <td class="nowrap">
                    ${formatDate(x.dueDate)}
                    ${x.isOverdue ? ` <span data-i18n="dashboard.overdue" class="badge red">Quá hạn</span>` : ''}
                </td>
            </tr>
        `).join('') : `<tr><td colspan="4">${emptyHtml(t('account.chuaCoKhoaHocNaoDuocPhanCong', 'Chưa có khóa học nào được phân công.'))}</td></tr>`;
    }

    function renderCertificate(certificateList) {
        const box = document.getElementById('cert-box');
        
        box.innerHTML = certificateList.length ? `<div class="grid grid-2">${certificateList.slice(0, 4).map(c => `
            <a href="/pages/certificate/certificate.html" class="cert-card interactive acc-cert-card ${c.isRevoked ? 'revoked' : ''}">
                <span class="cert-card-shine"></span>
                <i class="ph-fill ph-seal-check cert-card-stamp"></i>
                <div class="cert-card-no">
                    <i class="ph-fill ph-medal"></i> ${esc(c.certificateNo)}
                </div>
                <div class="cert-card-course acc-cert-title" title="${esc(c.courseTitle)}">
                    ${esc(c.courseTitle)}
                </div>
                <div class="acc-cert-date">
                    <i class="ph ph-calendar-blank"></i> Cấp ngày ${formatDate(c.issuedAt)}
                    ${c.isRevoked ? `<span data-i18n="account.daThuHoi" class="badge red" style="margin-left:auto; padding:2px 6px; font-size:10px;">Đã thu hồi</span>` : ''}
                </div>
            </a>
        `).join('')}</div>` : emptyHtml(t('account.banChuaCoChungNhanNao', 'Bạn chưa có chứng nhận nào.'), '<i class="ph ph-seal-check"></i>');
    }

    function openEdit() {
        document.getElementById('e-name').value = profile.fullName;
        document.getElementById('e-phone').value = profile.phoneNumber || '';
        document.getElementById('e-email').value = profile.email;
        openModal('modal-edit');
    }

    async function saveProfile(e) {
        e.preventDefault();
        try {
            profile = await API.put('/auth/me', {
                fullName: document.getElementById('e-name').value.trim(),
                phoneNumber: document.getElementById('e-phone').value.trim() || null,
                avatarUrl: profile.avatarUrl || null
            });
            renderProfile();
            toastOk(t('account.daCapNhatHoSo', 'Đã cập nhật hồ sơ.'));
            closeModal('modal-edit');
        } catch (ex) { toastErr(ex.message); }
    }

    async function savePassword(e) {
        e.preventDefault();
        const newPassword = document.getElementById('p-new').value;
        if (newPassword !== document.getElementById('p-confirm').value) {
            toastErr(t('account.xacNhanMatKhauMoiKhongKhop', 'Xác nhận mật khẩu mới không khớp.'));
            return;
        }

        // Báo ngay tại chỗ bằng đúng bộ quy tắc của máy chủ (js/password.js). Máy chủ vẫn kiểm lại — đây
        // chỉ để người dùng khỏi phải gửi đi rồi mới biết mình gõ chưa đạt.
        const profile = Auth.profile ?? {};
        const ruleError = passwordRuleError(newPassword, profile.email, profile.userName, profile.employeeCode);
        if (ruleError) { toastErr(ruleError); return; }
        try {
            await API.post('/auth/change-password', {
                currentPassword: document.getElementById('p-current').value,
                newPassword
            });
            toastOk(t('account.doiMatKhauThanhCong', 'Đổi mật khẩu thành công.'));
            closeModal('modal-password');
        } catch (ex) { toastErr(ex.message); }
    }

    async function uploadAvatar(e) {
        const file = e.target.files[0];
        if (!file) return;
        try {
            const uploaded = await API.upload(file, 'avatar');
            profile = await API.put('/auth/me', {
                fullName: profile.fullName,
                phoneNumber: profile.phoneNumber || null,
                avatarUrl: uploaded.url
            });
            renderProfile();
            toastOk(t('account.daCapNhatAnhDaiDien', 'Đã cập nhật ảnh đại diện.'));
        } catch (ex) { toastErr(ex.message); } finally { e.target.value = ''; }
    }
})();
