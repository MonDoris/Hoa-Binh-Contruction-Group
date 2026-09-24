(function () {
    'use strict';

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
})();
