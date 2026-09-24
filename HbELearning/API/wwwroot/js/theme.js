// File: wwwroot/js/theme.js
// ==================== CHẾ ĐỘ SÁNG / TỐI ====================
// Tách khỏi app.js vì hai trang công khai — login.html và index.html — không dựng vỏ ứng dụng nhưng vẫn
// phải có nút chuyển nền. File này không phụ thuộc app.js, chỉ cần nạp SAU language.js (để dùng được t()).
//
// Bảng màu nằm trọn trong CSS: css/style.css cho khu quản trị, css/login.css cho trang đăng nhập,
// css/index.css cho trang chủ. Ở đây chỉ lo ghi nhớ lựa chọn và gắn thuộc tính data-theme lên thẻ <html>.
//
// Ba trạng thái, không phải hai: "light" và "dark" là do người dùng chủ động chọn, còn "auto" (chưa chọn gì)
// thì đi theo cài đặt của máy. Gộp thành hai trạng thái thì người để máy ở chế độ tối vẫn bị ép nền trắng
// cho tới khi tự bấm nút.

const THEME_KEY = 'hbe_theme';

const Theme = {
    /** Lựa chọn đã lưu: 'light' | 'dark' | null (chưa chọn -> theo máy). */
    get saved() {
        const value = localStorage.getItem(THEME_KEY);
        return value === 'light' || value === 'dark' ? value : null;
    },

    /** Chế độ đang hiển thị thật sự, sau khi đã tính cả cài đặt của máy. */
    get current() {
        return this.saved
            ?? (window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    },

    /** @param {'light'|'dark'|null} value null = bỏ lựa chọn, quay về đi theo máy. */
    set(value) {
        if (value === null) localStorage.removeItem(THEME_KEY);
        else localStorage.setItem(THEME_KEY, value);
        applyTheme();
        refreshThemeSwitch();
    },

    toggle() { this.set(this.current === 'dark' ? 'light' : 'dark'); },
};

/** Gắn/gỡ thuộc tính trên <html>. Không có lựa chọn thì gỡ hẳn để media query của máy được quyền quyết định. */
function applyTheme() {
    const saved = Theme.saved;
    if (saved) document.documentElement.setAttribute('data-theme', saved);
    else document.documentElement.removeAttribute('data-theme');
    applyThemeColor();
}

/**
 * Đồng bộ màu thanh trạng thái của điện thoại khi hệ thống được cài như ứng dụng (PWA).
 * Không làm việc này thì thanh trạng thái iOS/Android vẫn xanh thương hiệu trong lúc trang đã sang nền
 * tối — nhìn như một dải màu lạ dán lên đầu app. Không đặt được bằng media query trong thẻ meta vì
 * người dùng còn tự chọn sáng/tối được, không chỉ đi theo cài đặt máy.
 */
function applyThemeColor() {
    const meta = document.querySelector('meta[name="theme-color"]');
    if (!meta) return;
    meta.setAttribute('content', Theme.current === 'dark' ? '#0f172a' : '#00a1e6');
}

// Chạy NGAY lúc nạp file, trước khi trang vẽ phần lớn nội dung. Mỗi trang còn có thêm một dòng nhỏ trong
// <head> làm việc này sớm hơn nữa, để không thấy một nháy nền trắng rồi mới chuyển sang tối.
applyTheme();

// Đang ở chế độ "theo máy" mà người dùng đổi cài đặt hệ điều hành thì đổi theo ngay, không cần tải lại trang.
window.matchMedia?.('(prefers-color-scheme: dark)').addEventListener?.('change', () => {
    if (!Theme.saved) { applyThemeColor(); refreshThemeSwitch(); }
});

// Mỗi trang đặt nút ở một chỗ khác nhau (topbar của khu quản trị, góc phải trang đăng nhập, thanh điều hướng
// trang chủ) nên vẽ lại theo class chung thay vì đi tìm đúng một id.
const THEME_SWITCH_SELECTOR = '.theme-switch, .theme-switch-login';

function refreshThemeSwitch() {
    document.querySelectorAll(THEME_SWITCH_SELECTOR).forEach(renderThemeSwitch);
}

/**
 * Dựng nút chuyển sáng/tối. Icon hiện chế độ SẮP chuyển sang (đang sáng thì hiện mặt trăng) — đó là quy ước
 * quen thuộc và cũng là thứ trả lời đúng câu hỏi "bấm vào thì được gì".
 */
function renderThemeSwitch(buttonEl) {
    if (!buttonEl) return;

    const isDark = Theme.current === 'dark';
    const label = typeof t === 'function'
        ? (isDark ? t('shell.themeToLight', 'Chuyển sang nền sáng') : t('shell.themeToDark', 'Chuyển sang nền tối'))
        : (isDark ? 'Chuyển sang nền sáng' : 'Chuyển sang nền tối');

    buttonEl.innerHTML = `<i class="ph-fill ${isDark ? 'ph-sun' : 'ph-moon'}"></i>`;
    buttonEl.title = label;
    buttonEl.setAttribute('aria-label', label);
    buttonEl.onclick = () => Theme.toggle();
}
