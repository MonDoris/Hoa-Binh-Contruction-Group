// File: wwwroot/js/password.js
// ============================================================================================
// WIDGET Ô NHẬP MẬT KHẨU DÙNG CHUNG: thanh đo độ mạnh + nút con mắt xem lại chuỗi vừa gõ.
//
// Vì sao tách riêng khỏi app.js: trang đăng nhập cố ý KHÔNG nạp app.js (nó không có thanh bên, không có
// menu, không cần quyền hạn — nạp cả app.js vào chỉ để lấy hai hàm là kéo theo toàn bộ phần vỏ hệ thống).
// Nhưng nó vẫn cần đúng widget này, nên trước đây login.html giữ một bản chép tay. Hai bản đó đã trôi khỏi
// nhau: cùng một mật khẩu có thể được chấm "Trung bình" ở trang này và "Mạnh" ở trang kia sau vài lần sửa
// một bên mà quên bên còn lại. Quy tắc chấm điểm phải là MỘT, nên nó ở đây.
//
// Khác biệt thật sự giữa hai nơi chỉ là chỗ ĐẶT nút con mắt và thanh đo — nên đó là tham số, không phải
// lý do để có hai bản.
// ============================================================================================

// ============================================================================================
// QUY TẮC MẬT KHẨU — bản sao phía trình duyệt của Domain/Common/PasswordPolicy.cs.
//
// Đây chỉ là bản BÁO SỚM cho người đang gõ, KHÔNG phải hàng rào: ai cũng gọi thẳng API được, nên máy chủ
// luôn kiểm lại bằng PasswordPolicy. Sửa quy tắc thì phải sửa cả hai chỗ, nếu không người dùng sẽ thấy ô
// nhập báo "hợp lệ" rồi máy chủ vẫn từ chối.
// ============================================================================================

/** Độ dài tối thiểu — khớp PasswordPolicy.MinLength. */
const PASSWORD_MIN_LENGTH = 8;

/** Câu mô tả quy tắc, hiện dưới ô nhập — khớp PasswordPolicy.Description. */
const PASSWORD_RULE_TEXT = 'Tối thiểu 8 ký tự, phải có cả chữ và số, không dùng mật khẩu dễ đoán.';

/** Những mật khẩu bị từ chối thẳng — khớp PasswordPolicy.CommonPasswordSet. */
const COMMON_PASSWORD_LIST = [
    '12345678', '123456789', '1234567890', 'password', 'password1', 'password123', 'passw0rd',
    'qwertyuiop', 'qwerty123', 'abcd1234', 'abc12345', '1qaz2wsx', 'zaq12wsx', 'iloveyou',
    'matkhau', 'matkhau1', 'matkhau123', 'matkhau@123', 'khongbiet', 'hoabinh', 'hoabinh123',
    'admin123', 'admin@123', 'administrator', 'welcome1', 'welcome123', 'letmein1', 'changeme',
    '111111111', '000000000', '88888888', '66668888', '12341234', 'asdfghjkl', 'hbelearning'
];

/**
 * Kiểm một mật khẩu sắp đặt.
 * @param {string} password
 * @param {...string} identifierList Email / tên tài khoản / mã nhân viên của chính người đó.
 * @returns {string|null} Câu báo lỗi, hoặc null nếu hợp lệ.
 */
function passwordRuleError(password, ...identifierList) {
    if (!password) return 'Chưa nhập mật khẩu.';
    if (password.length < PASSWORD_MIN_LENGTH) return `Mật khẩu phải có ít nhất ${PASSWORD_MIN_LENGTH} ký tự.`;
    if (password.length > 128) return 'Mật khẩu không được dài quá 128 ký tự.';
    if (password.trim() !== password)
        return 'Mật khẩu không được bắt đầu hoặc kết thúc bằng dấu cách — rất dễ gõ sai ở lần đăng nhập sau.';
    if (!/[A-Za-zÀ-ỹ]/.test(password) || !/\d/.test(password)) return 'Mật khẩu phải có cả chữ và số.';
    if (COMMON_PASSWORD_LIST.includes(password.toLowerCase()))
        return 'Mật khẩu này nằm trong danh sách bị đoán ra đầu tiên. Hãy chọn mật khẩu khác.';
    if (new Set(password).size < 4) return 'Mật khẩu dùng quá ít ký tự khác nhau. Hãy chọn mật khẩu khác.';

    for (const identifier of identifierList) {
        if (!identifier) continue;
        const value = String(identifier).split('@')[0].trim();
        if (value.length < 4) continue;
        if (password.toLowerCase().includes(value.toLowerCase()))
            return 'Mật khẩu không được chứa email, tên tài khoản hay mã nhân viên của chính bạn.';
    }
    return null;
}

/** Điểm 0-5 dựa trên độ dài và đa dạng ký tự (chữ hoa+thường, số, ký tự đặc biệt). */
function passwordStrengthScore(password) {
    if (!password) return 0;
    let score = 0;
    if (password.length >= PASSWORD_MIN_LENGTH) score++;
    if (password.length >= 10) score++;
    if (/[a-z]/.test(password) && /[A-Z]/.test(password)) score++;
    if (/\d/.test(password)) score++;
    if (/[^A-Za-z0-9]/.test(password)) score++;
    return score;
}

/** Quy 0-5 điểm về ba mức hiển thị. Ba mức chứ không phải năm: người dùng cần biết "đã đủ mạnh chưa",
 *  chia nhỏ hơn chỉ khiến thanh đo nhảy qua lại mà không gợi ý được phải sửa gì. */
function passwordStrengthLevel(score) {
    if (score <= 1) return { level: 'weak', label: t('pwd.weak', 'Yếu'), segmentCount: 1 };
    if (score <= 3) return { level: 'medium', label: t('pwd.medium', 'Trung bình'), segmentCount: 2 };
    return { level: 'strong', label: t('pwd.strong', 'Mạnh'), segmentCount: 3 };
}

/** Sự kiện nội bộ để vẽ lại icon con mắt — xem attachPasswordEye() và resetPasswordEye(). */
const PWD_EYE_REFRESH = 'pwd-eye-refresh';

/**
 * Gắn thanh đo độ mạnh (3 đoạn đỏ/vàng/xanh lá) ngay sau ô nhập, tự ẩn khi rỗng và cập nhật theo từng
 * ký tự gõ.
 *
 * @param {HTMLInputElement} inputEl
 * @param {object}   [options]
 * @param {string}   [options.anchorSelector] Bộ chọn của khối bao ngoài mà thanh đo phải đứng SAU. Mặc định
 *        là khung '.pwd-field' do attachPasswordEye() tạo ra. Trang đăng nhập truyền '.input-group' vì ở đó
 *        ô nhập nằm trong một khối có nhãn nổi — thả thanh đo vào trong khối ấy sẽ kéo dài nó ra và nút con
 *        mắt (định vị theo khối) bị đẩy lệch xuống.
 */
function attachPasswordStrength(inputEl, options = {}) {
    if (!inputEl || inputEl.dataset.strengthAttached) return;
    inputEl.dataset.strengthAttached = '1';

    const box = document.createElement('div');
    box.className = 'pwd-strength';
    box.innerHTML = `<div class="pwd-strength-bar"><span></span><span></span><span></span></div><div class="pwd-strength-label"></div>`;

    const anchor = inputEl.closest(options.anchorSelector || '.pwd-field') || inputEl;
    anchor.insertAdjacentElement('afterend', box);

    const segmentList = box.querySelectorAll('.pwd-strength-bar span');
    const label = box.querySelector('.pwd-strength-label');
    const bar = box.querySelector('.pwd-strength-bar');

    const update = () => {
        const value = inputEl.value;
        box.classList.toggle('show', !!value);
        if (!value) {
            segmentList.forEach(s => s.classList.remove('on'));
            bar.className = 'pwd-strength-bar';
            return;
        }

        const { level, label: text } = passwordStrengthLevel(passwordStrengthScore(value));
        segmentList.forEach(s => s.classList.add('on'));
        bar.className = 'pwd-strength-bar ' + level;
        label.textContent = text;
        label.className = 'pwd-strength-label ' + level;
    };

    inputEl.addEventListener('input', update);
    update();
}

/**
 * Gắn nút con mắt vào ô nhập mật khẩu: bấm để đọc lại chuỗi vừa gõ.
 *
 * Mật khẩu bị che là đúng, nhưng gõ sai một ký tự thì không có cách nào biết ngoài việc xóa hết gõ lại —
 * càng dễ sai với mật khẩu dài. Icon là con mắt ĐANG mở khi chữ còn bị che (bấm vào thì nhìn thấy) và
 * gạch chéo khi chữ đang hiện, tức là luôn mô tả việc SẼ xảy ra khi bấm.
 *
 * @param {HTMLInputElement} inputEl
 * @param {object} [options]
 * @param {string} [options.hostSelector] Bộ chọn của khối đã có sẵn để thả nút vào. Bỏ trống thì hàm tự bọc
 *        ô nhập trong một khung '.pwd-field' mới — cách này gọn nhất vì nút bám theo mép phải của chính ô
 *        nhập. Trang đăng nhập PHẢI truyền '.input-group': nhãn nổi ở đó dựa vào bộ chọn "input ~ label",
 *        bọc thêm một thẻ là hai phần tử hết còn là anh em và nhãn đứng im không chịu nổi lên.
 */
function attachPasswordEye(inputEl, options = {}) {
    if (!inputEl || inputEl.dataset.eyeAttached) return;

    let host = options.hostSelector ? inputEl.closest(options.hostSelector) : null;
    if (options.hostSelector && !host) return;   // trang gọi chỉ định chỗ đặt mà không có → không gắn bừa

    if (!host) {
        host = document.createElement('div');
        host.className = 'pwd-field';
        inputEl.insertAdjacentElement('beforebegin', host);
        host.appendChild(inputEl);
    }
    inputEl.dataset.eyeAttached = '1';

    const button = document.createElement('button');
    // type="button": nút nằm trong <form>, để mặc định là submit thì bấm xem mật khẩu hóa ra gửi luôn biểu mẫu.
    button.type = 'button';
    button.className = 'pwd-eye';
    host.appendChild(button);

    const render = () => {
        const shown = inputEl.type === 'text';
        button.innerHTML = `<i class="ph ${shown ? 'ph-eye-slash' : 'ph-eye'}"></i>`;
        button.title = shown ? t('pwd.an', 'Ẩn mật khẩu') : t('pwd.hien', 'Hiện mật khẩu');
        button.setAttribute('aria-label', button.title);
        button.setAttribute('aria-pressed', String(shown));
    };

    button.addEventListener('click', () => {
        inputEl.type = inputEl.type === 'password' ? 'text' : 'password';
        render();
        inputEl.focus();
    });

    // Cho phép vẽ lại icon từ bên ngoài (resetPasswordEye) mà không phải phơi hàm render ra ngoài.
    button.addEventListener(PWD_EYE_REFRESH, render);

    render();
}

/**
 * Che lại mọi mật khẩu đang hiện trong một vùng. Gọi mỗi lần mở hộp thoại: mật khẩu của lần trước còn
 * đang phơi ra mà hộp thoại mở lại là lộ chuỗi vừa gõ cho người ngồi cạnh, dù ô đã được xóa trắng.
 *
 * Dò theo dấu data-eye-attached chứ không theo tên khung bao ngoài: khung đó là '.pwd-field' hay
 * '.input-group' tùy trang, còn dấu này thì hàm gắn nút nào cũng để lại.
 */
function resetPasswordEye(root) {
    root?.querySelectorAll('input[type="text"][data-eye-attached]').forEach(input => {
        input.type = 'password';
        input.parentElement?.querySelector('.pwd-eye')?.dispatchEvent(new Event(PWD_EYE_REFRESH));
    });
}
