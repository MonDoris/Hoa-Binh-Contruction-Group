// File: wwwroot/pages/login/login.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    // Đã đăng nhập thì vào thẳng hệ thống
    if (Auth.isLoggedIn) { location.href = '/pages/dashboard/dashboard.html'; return; }
    applyI18n();

    // Trang chưa đăng nhập nên không có topbar — nút đổi ngôn ngữ phải đặt riêng.
    renderLanguageSwitch(document.getElementById('langSwitch'));
    renderThemeSwitch(document.getElementById('themeSwitch'));

    const form   = document.getElementById('authForm');
    const logo   = document.getElementById('hbcLogo');
    const gate   = document.getElementById('gate');
    const btn    = document.getElementById('submitBtn');
    const card   = document.getElementById('loginCard');
    const errBox = document.getElementById('authError');

    const emailInput    = document.getElementById('email');
    const passwordInput = document.getElementById('password');
    const rememberBox   = document.getElementById('rememberMe');

    const attemptBox    = document.getElementById('attemptBox');
    const attemptNumber = document.getElementById('attemptNumber');

    // Tài khoản vừa bị máy chủ báo là đã khóa. Giữ lại để biểu mẫu không mở lại sau đó: không cho gửi tiếp,
    // không xóa câu thông báo khi người dùng gõ lại, và nút Đăng nhập ở nguyên trạng thái mờ.
    let isLockedOut = false;

    const MIN_DECODE_MS  = 500;    // giữ hiệu ứng giải mã tối thiểu cho đủ "đã mắt"
    const REDIRECT_AFTER = 1100;   // thời gian xem hoạt cảnh mở cửa trước khi chuyển trang

    // ==================== BĂNG CHUYỀN ẢNH ====================
    // Chỉ là trang trí cho nửa trái, không dính gì tới đăng nhập — nên nó đứng đầu và không có nhánh nào
    // của phần xác thực bên dưới phụ thuộc vào nó. Màn hình nhỏ giấu hẳn nửa trái, lúc đó vòng lặp vẫn
    // chạy nhưng đẩy một khối display:none, vô hại và rẻ hơn là đi nghe sự kiện đổi bề ngang màn hình.
    (function initHeroSlider() {
        const slider = document.getElementById('heroSlider');
        const dotBox = document.getElementById('heroDots');
        if (!slider || !dotBox) return;

        const total = slider.children.length;
        let current = 0;
        let timer = null;

        const dots = Array.from({ length: total }, (_, i) => {
            const dot = document.createElement('button');
            dot.type = 'button';
            dot.setAttribute('aria-label', 'Ảnh ' + (i + 1));
            dot.addEventListener('click', () => { goTo(i); restart(); });
            dotBox.appendChild(dot);
            return dot;
        });

        function goTo(index) {
            current = (index + total) % total;
            slider.style.transform = `translateX(-${current * 100}%)`;
            dots.forEach((d, i) => d.classList.toggle('active', i === current));
        }

        // Bấm chọn ảnh xong mà 1 giây sau ảnh tự nhảy tiếp thì cú bấm coi như bị phớt lờ — đếm lại từ đầu.
        function restart() { clearInterval(timer); timer = setInterval(() => goTo(current + 1), 6000); }

        goTo(0);
        restart();
    })();

    // ==================== GHI NHỚ TÀI KHOẢN ====================
    // Chỉ nhớ phần ĐỊNH DANH để khỏi gõ lại, không bao giờ nhớ mật khẩu và cũng không kéo dài phiên đăng
    // nhập — thời hạn của phiên do token quyết định (xem js/api.js).
    const REMEMBER_KEY = 'hbe_login_email';
    const rememberedEmail = localStorage.getItem(REMEMBER_KEY);
    if (rememberedEmail) {
        emailInput.value = rememberedEmail;
        rememberBox.checked = true;
        passwordInput.focus();
    } else {
        emailInput.focus();
    }
    // Bỏ tick là xóa ngay, không đợi tới lần đăng nhập sau: người bỏ tick thường đang muốn xóa dấu vết
    // trên máy dùng chung, bắt họ đăng nhập thêm một lần nữa mới xóa là hiểu sai ý.
    rememberBox.addEventListener('change', () => {
        if (!rememberBox.checked) localStorage.removeItem(REMEMBER_KEY);
    });

    // Nút điền nhanh tài khoản dùng thử
    document.querySelectorAll('[data-fill]').forEach(button => {
        button.addEventListener('click', () => {
            if (isLockedOut) return;
            const [email, password] = button.dataset.fill.split('|');
            emailInput.value = email;
            passwordInput.value = password;
            hideError();
            emailInput.dispatchEvent(new Event('input'));
        });
    });

    [emailInput, passwordInput].forEach(el => el.addEventListener('input', hideError));

    /** Vẽ lại số lần còn được gõ sai. Về 0 thì tô đỏ cả dòng, giống cách ô báo lỗi tự hiện màu.
     *
     *  Hàm này cũng là chỗ DUY NHẤT làm dòng đếm hiện ra: lúc mới mở trang chưa có gì để cảnh báo, hiện sẵn
     *  một hạn mức chỉ làm người dùng bình thường hoang mang. Chỉ khi máy chủ đã tính một lần gõ sai thì con
     *  số mới xuất hiện — và lúc đó nó là số thật, không phải số đoán ở trình duyệt. */
    function setRemainingAttempt(remaining) {
        attemptNumber.textContent = remaining;
        attemptBox.classList.toggle('warn', remaining <= 1);
        attemptBox.classList.add('show');
    }

    /** Đóng hẳn biểu mẫu sau lần sai cuối cùng. Máy chủ mới là nơi thực sự chặn (xem AuthService.Login),
     *  đây chỉ là để người dùng khỏi gõ thêm vô ích. Chỉ phòng IT mở lại được nên không có đường quay lui. */
    function lockOut() {
        isLockedOut = true;
        setRemainingAttempt(0);
        [emailInput, passwordInput].forEach(el => el.disabled = true);
        btn.disabled = true;
        // .locked để CSS biết nút mờ đi vì đã khóa hẳn chứ không phải đang chờ máy chủ — lúc chờ nút sáng và quay
        // lúc này sẽ nói rằng còn thứ gì đó đang xử lý, trong khi không còn gì cả.
        btn.classList.add('locked');
        btn.textContent = t('login.lockedBtn', 'Tài khoản đã bị khóa');
    }

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (isLockedOut) return;
        hideError();

        const email = emailInput.value.trim();
        const password = passwordInput.value;

        if (!email || !password) { fail(t('login.errRequired', 'Vui lòng nhập đầy đủ định danh và khóa bảo mật.')); return; }
        // Định danh là email HOẶC tên tài khoản. Tên tài khoản không bao giờ chứa "@" (xem Helpers/UserNameHelper.cs),
        // nên chỉ chuỗi có "@" mới bị đo bằng thước email — gõ thiếu chữ ".vn" thì báo ngay tại chỗ thay vì
        // phải chờ máy chủ trả lời "không tìm thấy tài khoản".
        if (email.includes('@') && !/^\S+@\S+\.\S+$/.test(email)) { fail(t('login.errEmail', 'Địa chỉ email không hợp lệ.')); return; }

        // ----- Trạng thái đang giải mã -----
        btn.disabled = true;
        btn.textContent = t('login.decoding', 'Đang giải mã...');

        logo.classList.add('spin-hyper');

        const startedAt = Date.now();

        try {
            const result = await API.post('/auth/login', { email, password });
            Auth.save(result.token, result.profile);

            // Ghi nhớ định danh sau khi biết chắc nó đúng — nhớ một chuỗi gõ sai thì lần sau lại phải xóa đi.
            if (rememberBox.checked) localStorage.setItem(REMEMBER_KEY, email);
            else localStorage.removeItem(REMEMBER_KEY);

            // Đảm bảo hiệu ứng chạy đủ lâu rồi mới mở cửa
            const elapsed = Date.now() - startedAt;
            setTimeout(() => openGate(result.profile), Math.max(0, MIN_DECODE_MS - elapsed));

        } catch (ex) {
            // Giữ hiệu ứng xoay đủ lâu để mắt kịp thấy, kể cả khi đăng nhập thất bại
            const elapsed = Date.now() - startedAt;
            setTimeout(() => {
                logo.classList.remove('spin-hyper');

                // Đăng nhập sai trả về LoginFailedResponse (xem Application/DTOs/AuthDto.cs): ngoài câu thông
                // báo còn kèm số lần còn lại và cờ đã khóa. Lỗi hạ tầng (mất mạng, 500...) không có hai thứ
                // đó — khi ấy giữ nguyên con số đang hiện, vì lần bấm này không hề bị máy chủ tính là sai.
                const failure = (ex.data && typeof ex.data === 'object') ? ex.data : null;
                if (typeof failure?.remainingAttempt === 'number') setRemainingAttempt(failure.remainingAttempt);

                if (failure?.isLockedOut) {
                    lockOut();
                    fail(t('login.locked', 'Bạn đã bị khóa tài khoản, xin liên hệ phòng IT.'));
                    return;
                }
                fail(ex.message || t('login.errAuth', 'Không xác thực được. Vui lòng thử lại.'));
            }, Math.max(0, MIN_DECODE_MS - elapsed));
        }
    });

    function openGate(profile) {
        document.getElementById('sanctumText').textContent =
            t('login.hello', 'Xin chào, {name}', { name: profile.fullName });

        // roleLabel() dịch tên vai trò để HIỂN THỊ, chuỗi gốc profile.roleName vẫn giữ nguyên cho phân quyền.
        const who = roleLabel(profile.roleName) + (profile.departmentName ? ' · ' + profile.departmentName : '');
        document.getElementById('sanctumSub').textContent =
            t('login.opening', '{who} — đang mở không gian làm việc...', { who });

        // Logo vẫn xoay xuyên suốt lúc cánh cửa bật mở — chỉ dừng khi đăng nhập thất bại
        gate.classList.add('access-granted');

        const go = () => location.href = '/pages/dashboard/dashboard.html';
        const timer = setTimeout(go, REDIRECT_AFTER);

        // Bấm vào đâu cũng vào ngay, không cần đợi hết hoạt cảnh
        document.addEventListener('click', () => { clearTimeout(timer); go(); }, { once: true });
    }

    function fail(message) {
        // Đã khóa thì nút giữ nguyên trạng thái mờ mà lockOut() vừa đặt — mở lại là mời người dùng gõ thêm
        // một lần nữa để nhận đúng câu thông báo cũ.
        if (!isLockedOut) {
            btn.disabled = false;
            btn.textContent = t('login.activate', 'Kích hoạt');
        }
        errBox.textContent = message;
        errBox.classList.add('show');
        card.classList.remove('shake');
        void card.offsetWidth;          // ép trình duyệt tính lại để animation chạy lại
        card.classList.add('shake');
    }

    // Câu báo khóa tài khoản phải ở lại trên màn hình: nó không phải lỗi của lần gõ này mà là trạng thái
    // của cả tài khoản, gõ lại chữ nào cũng không làm nó hết đúng.
    function hideError() { if (!isLockedOut) errBox.classList.remove('show'); }

    // ==================== QUÊN MẬT KHẨU ====================
    const fpMask = document.getElementById('fpMask');
    const fpStepList = [1, 2, 3, 4].map(n => document.getElementById('fpStep' + n));
    const fpEmailInput = document.getElementById('fpEmail');
    const fpOtpInput = document.getElementById('fpOtp');
    const fpNewPasswordInput = document.getElementById('fpNewPassword');
    const fpConfirmPasswordInput = document.getElementById('fpConfirmPassword');
    const fpSendOtpBtn = document.getElementById('fpSendOtp');
    const fpVerifyOtpBtn = document.getElementById('fpVerifyOtp');
    const fpResetPasswordBtn = document.getElementById('fpResetPassword');
    const fpResendBtn = document.getElementById('fpResend');

    let fpResetToken = null;
    let fpResendTimer = null;
    let fpChannel = 'zalo';

    // ---------- Ô nhập mật khẩu ----------
    // Thanh đo và nút con mắt dùng chung với phần còn lại của hệ thống (js/password.js). Trang này chỉ
    // đặt chỗ khác đi:
    //   • nút con mắt thả vào '.field' — khối bọc RIÊNG ô nhập, nên height:100% của nút bằng đúng chiều
    //     cao ô nhập; thả vào '.input-group' thì khối đó còn ôm cả nhãn đứng bên trên và nút sẽ tụt lệch.
    //   • thanh đo đứng sau cả '.input-group' để không chen vào giữa ô nhập và nút con mắt.
    const PWD_HOST = { hostSelector: '.field', anchorSelector: '.input-group' };

    attachPasswordStrength(fpNewPasswordInput, PWD_HOST);
    [passwordInput, fpNewPasswordInput, fpConfirmPasswordInput].forEach(x => attachPasswordEye(x, PWD_HOST));

    document.querySelectorAll('.fp-channel').forEach(btn => {
        btn.addEventListener('click', () => {
            fpChannel = btn.dataset.channel;
            document.querySelectorAll('.fp-channel').forEach(b => b.classList.toggle('active', b === btn));
        });
    });

    document.getElementById('btnForgotPassword').addEventListener('click', openForgotPassword);
    document.getElementById('fpClose').addEventListener('click', closeForgotPassword);
    document.getElementById('fpDone').addEventListener('click', closeForgotPassword);
    fpMask.addEventListener('click', (e) => { if (e.target === fpMask) closeForgotPassword(); });

    function openForgotPassword() {
        fpEmailInput.value = emailInput.value.trim();
        goToFpStep(1);
        fpMask.classList.add('open');
    }

    function closeForgotPassword() {
        fpMask.classList.remove('open');
        clearInterval(fpResendTimer);
        fpResetToken = null;
        [fpEmailInput, fpOtpInput, fpNewPasswordInput, fpConfirmPasswordInput].forEach(el => el.value = '');
        resetPasswordEye(fpMask);
        fpNewPasswordInput.dispatchEvent(new Event('input'));
        [1, 2, 3].forEach(fpHideError);
    }

    function goToFpStep(n) { fpStepList.forEach((el, i) => el.hidden = (i + 1) !== n); }

    function fpShowError(step, message) {
        const box = document.getElementById('fpError' + step);
        box.textContent = message;
        box.classList.add('show');
    }
    function fpHideError(step) { document.getElementById('fpError' + step).classList.remove('show'); }

    async function sendForgotPasswordOtp() {
        const email = fpEmailInput.value.trim();
        await API.post('/auth/forgot-password', { email, channel: fpChannel });
        document.getElementById('fpStep2Email').textContent = email;
        document.getElementById('fpStep2Channel').textContent = fpChannel === 'sms' ? 'SMS' : fpChannel === 'email' ? 'Email' : 'Zalo';
        startFpResendCooldown();
    }

    fpSendOtpBtn.addEventListener('click', async () => {
        fpHideError(1);
        const email = fpEmailInput.value.trim();
        if (!email || !/^\S+@\S+\.\S+$/.test(email)) { fpShowError(1, t('fp.errEmail', 'Vui lòng nhập email hợp lệ.')); return; }

        fpSendOtpBtn.disabled = true;
        fpSendOtpBtn.textContent = t('fp.sending', 'Đang gửi...');
        try {
            await sendForgotPasswordOtp();
            goToFpStep(2);
        } catch (ex) {
            fpShowError(1, ex.message || t('fp.errSendOtp', 'Không gửi được mã OTP. Vui lòng thử lại.'));
        } finally {
            fpSendOtpBtn.disabled = false;
            fpSendOtpBtn.textContent = t('fp.sendOtp', 'Gửi mã OTP');
        }
    });

    fpResendBtn.addEventListener('click', async () => {
        if (fpResendBtn.disabled) return;
        fpHideError(2);
        try { await sendForgotPasswordOtp(); }
        catch (ex) { fpShowError(2, ex.message || t('fp.errResend', 'Không gửi lại được mã OTP.')); }
    });

    function startFpResendCooldown() {
        clearInterval(fpResendTimer);
        let remain = 60;
        fpResendBtn.disabled = true;
        fpResendBtn.textContent = t('fp.resendIn', 'Gửi lại mã ({second}s)', { second: remain });
        fpResendTimer = setInterval(() => {
            remain--;
            if (remain <= 0) {
                clearInterval(fpResendTimer);
                fpResendBtn.disabled = false;
                fpResendBtn.textContent = t('fp.resend', 'Gửi lại mã');
            } else {
                fpResendBtn.textContent = t('fp.resendIn', 'Gửi lại mã ({second}s)', { second: remain });
            }
        }, 1000);
    }

    fpVerifyOtpBtn.addEventListener('click', async () => {
        fpHideError(2);
        const code = fpOtpInput.value.trim();
        if (!/^\d{6}$/.test(code)) { fpShowError(2, t('fp.errOtpFormat', 'Mã OTP gồm 6 chữ số.')); return; }

        fpVerifyOtpBtn.disabled = true;
        fpVerifyOtpBtn.textContent = t('fp.confirming', 'Đang xác nhận...');
        try {
            const r = await API.post('/auth/verify-otp', { email: fpEmailInput.value.trim(), code });
            fpResetToken = r.resetToken;
            clearInterval(fpResendTimer);
            goToFpStep(3);
        } catch (ex) {
            fpShowError(2, ex.message || t('fp.errOtp', 'Mã OTP không đúng hoặc đã hết hạn.'));
        } finally {
            fpVerifyOtpBtn.disabled = false;
            fpVerifyOtpBtn.textContent = t('fp.confirm', 'Xác nhận');
        }
    });

    fpResetPasswordBtn.addEventListener('click', async () => {
        fpHideError(3);
        const newPassword = fpNewPasswordInput.value;
        const confirmPassword = fpConfirmPasswordInput.value;
        // Kiểm bằng đúng bộ quy tắc của máy chủ (password.js) để người dùng không bị đá lại sau khi đã
        // chờ OTP — máy chủ vẫn kiểm lại bằng PasswordPolicy.
        const pwdError = passwordRuleError(newPassword, fpEmailInput.value.trim());
        if (pwdError) { fpShowError(3, pwdError); return; }
        if (newPassword !== confirmPassword) { fpShowError(3, t('fp.errPwdMismatch', 'Mật khẩu xác nhận không khớp.')); return; }

        fpResetPasswordBtn.disabled = true;
        fpResetPasswordBtn.textContent = t('fp.processing', 'Đang xử lý...');
        try {
            await API.post('/auth/reset-password', { resetToken: fpResetToken, newPassword });
            goToFpStep(4);
        } catch (ex) {
            fpShowError(3, ex.message || t('fp.errReset', 'Không đặt lại được mật khẩu.'));
        } finally {
            fpResetPasswordBtn.disabled = false;
            fpResetPasswordBtn.textContent = t('fp.reset', 'Đặt lại mật khẩu');
        }
    });

})();
