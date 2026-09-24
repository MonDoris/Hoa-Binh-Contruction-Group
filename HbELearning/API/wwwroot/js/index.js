(async function () {
    // Trang chủ không dựng layout chung nên applyI18n() không tự chạy — phải chờ từ điển rồi gọi tay.
    await Lang.ready;
    applyI18n();

    // Trang chủ chưa đăng nhập nên không có topbar — nút đổi ngôn ngữ phải đặt riêng ở thanh điều hướng.
    const langBtn = document.getElementById('langSwitch');
    if (langBtn) renderLanguageSwitch(langBtn);
    renderThemeSwitch(document.getElementById('themeSwitch'));

    // ---------- Số liệu thật lấy từ API công khai ----------
    // Gán theo ID chứ không theo thứ tự phần tử: trang chủ chỉ bày một phần trong năm chỉ số, đếm theo
    // thứ tự thì bỏ bớt một thẻ là mọi con số phía sau nhảy sang sai ô mà không có gì báo lỗi.
    (async function loadStat() {
        try {
            const s = await API.get('/report/public-stat');
            const value = {
                'stat-course': s.publishedCourse,
                'stat-lesson': s.totalLesson,
                'stat-quiz': s.totalQuiz,
                'stat-enroll': s.completedEnrollment,
                'stat-cert': s.totalCertificate,
            };
            for (const [id, number] of Object.entries(value)) {
                const el = document.getElementById(id);
                if (el) countUp(el, number ?? 0);
            }
        } catch {
            const box = document.getElementById('lp-stat');
            if (box) box.style.display = 'none';
        }
    })();

    function countUp(el, target) {
        if (!target) { el.textContent = '0'; return; }
        const step = Math.max(1, Math.ceil(target / 26));
        let current = 0;
        const timer = setInterval(() => {
            current = Math.min(target, current + step);
            el.textContent = current;
            if (current >= target) clearInterval(timer);
        }, 28);
    }

    // ---------- Tra cứu chứng nhận ----------
    const input = document.getElementById('lp-code');
    const box = document.getElementById('lp-verify-result');

    document.getElementById('lp-btn-verify').addEventListener('click', verify);
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') verify(); });

    // Assuming qs is defined in your app.js, fallback if not
    const code = (typeof qs === 'function') ? qs('code') : new URLSearchParams(window.location.search).get('code');
    if (code) { input.value = code; verify(); document.getElementById('verify').scrollIntoView(); }

    // Helpers in case they are not in app.js
    const esc = (str) => String(str).replace(/[&<>'"]/g, match => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[match]));
    const formatDate = (dateStr) => new Date(dateStr).toLocaleDateString('vi-VN');

    async function verify() {
        const value = input.value.trim();
        if (!value) { box.innerHTML = '<div style="padding: 16px; background: var(--lp-bad-bg); color: var(--lp-bad-text); border-radius: 8px; text-align:center;">Vui lòng nhập mã xác thực.</div>'; return; }

        box.innerHTML = '<div style="text-align: center; color: var(--lp-text-muted);"><i class="ph ph-spinner ph-spin"></i> Đang xử lý kiểm tra...</div>';
        try {
            const r = await API.get(`/certificate/verify/${encodeURIComponent(value)}`);
            box.innerHTML = `
                <div style="background: ${r.isValid ? 'var(--lp-ok-bg)' : 'var(--lp-bad-bg)'}; border: 1px solid ${r.isValid ? 'var(--lp-ok-border)' : 'var(--lp-bad-border)'}; padding: 20px; border-radius: 12px;">
                    <b style="color: ${r.isValid ? 'var(--lp-ok-text)' : 'var(--lp-bad-text)'}; display: flex; align-items: center; gap: 8px; margin-bottom: 16px; font-size: 1.1rem;">
                        <i class="ph-fill ${r.isValid ? 'ph-check-circle' : 'ph-x-circle'}" style="font-size: 1.4rem;"></i> ${esc(r.message)}
                    </b>
                    ${r.certificateNo ? `<table style="width: 100%; border-collapse: collapse; font-size: 14px;">
                        <tr style="border-bottom: 1px solid var(--lp-row-border);"><th style="text-align: left; padding: 12px 8px; color: var(--lp-th);">Số hiệu</th><td style="padding: 12px 8px; font-weight: 500;">${esc(r.certificateNo)}</td></tr>
                        ${r.userFullName ? `<tr style="border-bottom: 1px solid var(--lp-row-border);"><th style="text-align: left; padding: 12px 8px; color: var(--lp-th);">Người được cấp</th><td style="padding: 12px 8px; font-weight: 500;">${esc(r.userFullName)}</td></tr>` : ''}
                        ${r.courseTitle ? `<tr style="border-bottom: 1px solid var(--lp-row-border);"><th style="text-align: left; padding: 12px 8px; color: var(--lp-th);">Khóa đào tạo</th><td style="padding: 12px 8px; font-weight: 500;">${esc(r.courseTitle)}</td></tr>` : ''}
                        ${r.finalScore != null ? `<tr style="border-bottom: 1px solid var(--lp-row-border);"><th style="text-align: left; padding: 12px 8px; color: var(--lp-th);">Đánh giá</th><td style="padding: 12px 8px; font-weight: 500;">${r.finalScore}/100</td></tr>` : ''}
                        ${r.issuedAt ? `<tr><th style="text-align: left; padding: 12px 8px; color: var(--lp-th);">Ngày cấp</th><td style="padding: 12px 8px; font-weight: 500;">${formatDate(r.issuedAt)}</td></tr>` : ''}
                    </table>` : ''}
                </div>`;
        } catch (ex) {
            box.innerHTML = `<div style="padding: 16px; background: var(--lp-bad-bg); color: var(--lp-bad-text); border-radius: 8px; text-align:center;"><i class="ph-fill ph-warning-circle"></i> ${esc(ex.message || 'Lỗi hệ thống khi kiểm tra mã.')}</div>`;
        }
    }

    // ---------- Cuộn mượt tới các mục ----------
    // Trừ chiều cao thanh điều hướng vì nó nổi cố định trên đầu trang: cuộn đúng offsetTop là tiêu đề
    // mục chui xuống dưới thanh đó.
    const nav = document.querySelector('.modern-nav');
    document.querySelectorAll('.modern-nav a[href^="#"]').forEach(link => {
        link.addEventListener('click', (e) => {
            e.preventDefault();
            const targetEl = document.querySelector(link.getAttribute('href'));
            if (targetEl) {
                const navHeight = (nav?.offsetHeight ?? 0) + 32;
                window.scrollTo({ top: targetEl.offsetTop - navHeight, behavior: 'smooth' });
            }
        });
    });

    // ---------- Đã đăng nhập thì đổi nút thành vào hệ thống ----------
    if (typeof Auth !== 'undefined' && Auth.isLoggedIn) {
        document.querySelectorAll('a[href="/login.html"]').forEach(a => {
            a.setAttribute('href', '/pages/dashboard.html');
            if (a.classList.contains('btn-primary')) a.innerHTML = `${t('home.toDashboard', 'Vào Dashboard')} <i class="ph ph-arrow-right"></i>`;
        });
    }
})();