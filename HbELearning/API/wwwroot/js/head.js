(function () {
    'use strict';

    // Cỡ chữ (S/M/L) đặt lên <html> ngay từ <head> — để tới theme.js (cuối trang) mới đặt thì nội dung đã vẽ ở cỡ
    // thường rồi mới nhảy cỡ. Xem TextSize trong js/theme.js.
    try {
        const size = localStorage.getItem('hbe_text_size');
        if (size === 's' || size === 'l') document.documentElement.setAttribute('data-text-size', size);
    } catch { /* trình duyệt chặn localStorage: giữ cỡ thường */ }

    document.head.insertAdjacentHTML('beforeend', `
        <!-- Màu thanh trạng thái khi cài như app — js/theme.js cập nhật lại mỗi lần đổi nền sáng/tối. -->
        <meta name="theme-color" content="#00a1e6">
        <meta name="apple-mobile-web-app-capable" content="yes">
        <meta name="mobile-web-app-capable" content="yes">
        <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
        <meta name="apple-mobile-web-app-title" content="HB Learn">
        <link rel="manifest" href="/manifest.webmanifest">
        <link rel="icon" href="/img/icons/icon-192.png">
        <link rel="apple-touch-icon" href="/img/icons/apple-touch-icon.png">`);

    const sw = document.createElement('script');
    sw.src = '/js/pwa.js';
    sw.defer = true;
    document.head.appendChild(sw);

    // Emoji thay icon trang trí (điều hướng và nút bấm vẫn giữ icon) — xem js/emoji.js.
    const emoji = document.createElement('script');
    emoji.src = '/js/emoji.js';
    emoji.defer = true;
    document.head.appendChild(emoji);
})();
