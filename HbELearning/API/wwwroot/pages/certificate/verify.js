// File: wwwroot/pages/certificate/verify.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    ensurePhosphorIcons();
    ensureUicons();

    const input = document.getElementById('code');
    const box = document.getElementById('result');

    document.getElementById('btn-verify').addEventListener('click', verify);
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') verify(); });

    const code = qs('code');
    if (code) { input.value = code; verify(); }

    async function verify() {
        const value = input.value.trim();
        if (!value) { toastErr(t('verify.vuiLongNhapMaXacThuc', 'Vui lòng nhập mã xác thực.')); return; }

        box.innerHTML = skeletonHtml('block', { height: '180px' });
        try {
            const r = await API.get(`/certificate/verify/${encodeURIComponent(value)}`);

            // Giao diện kết quả VIP mới
            box.innerHTML = `
                <div class="cert-card cert-res-card ${r.isValid ? 'valid' : 'invalid'}">
                    ${r.isValid ? '<span class="cert-card-shine"></span>' : ''}
                    <i class="ph-fill ${r.isValid ? 'ph-seal-check' : 'ph-warning-octagon'} cert-card-stamp"></i>
                    
                    <div class="res-head">
                        <div class="res-icon">
                            <i class="ph-bold ${r.isValid ? 'ph-check' : 'ph-x'}"></i>
                        </div>
                        <h4 class="res-msg">${esc(r.message)}</h4>
                    </div>

                    ${r.certificateNo ? `
                    <div class="cert-card-no res-id">
                        <i class="ph-fill ph-medal"></i> ${esc(r.certificateNo)}
                    </div>
                    
                    <div class="res-grid">
                        ${r.userFullName ? `
                            <div class="res-col">
                                <span class="lbl" data-i18n="verify.nguoiDuocCap">Người được cấp</span>
                                <span class="val">${esc(r.userFullName)} <small style="color:var(--text-muted); font-weight:normal; display:block;">${esc(r.employeeCode || '')}</small></span>
                            </div>
                        ` : ''}
                        
                        ${r.finalScore != null ? `
                            <div class="res-col highlight">
                                <span class="lbl" data-i18n="verify.ketQua">Kết quả (Điểm)</span>
                                <span class="val">${r.finalScore}<small style="font-size:12px">/100</small></span>
                            </div>
                        ` : ''}

                        ${r.courseTitle ? `
                            <div class="res-col full">
                                <span class="lbl" data-i18n="verify.khoaDaoTao">Khóa đào tạo hoàn thành</span>
                                <span class="val">${esc(r.courseTitle)}</span>
                            </div>
                        ` : ''}
                        
                        ${r.issuedAt ? `
                            <div class="res-col">
                                <span class="lbl" data-i18n="verify.ngayCap">Ngày cấp</span>
                                <span class="val">${formatDate(r.issuedAt)}</span>
                            </div>
                        ` : ''}
                        
                        ${r.expiredAt ? `
                            <div class="res-col">
                                <span class="lbl" data-i18n="verify.hieuLucDen">Hiệu lực đến</span>
                                <span class="val">${formatDate(r.expiredAt)}</span>
                            </div>
                        ` : ''}
                    </div>
                    ` : ''}
                </div>`;
        } catch (ex) {
            box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        }
    }
})();
