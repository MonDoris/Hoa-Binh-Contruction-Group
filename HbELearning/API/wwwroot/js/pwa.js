// File: wwwroot/js/pwa.js
// Đăng ký service worker và lo phần "cài như app". Nạp trên MỌI trang, đứng độc lập — không phụ thuộc
// api.js/app.js để trang đăng nhập và trang giới thiệu cũng dùng được.

(function () {
    'use strict';

    // Service worker chỉ chạy trên HTTPS hoặc localhost. Chạy dev qua http://192.168.x.x để thử trên
    // điện thoại thật thì trình duyệt chặn — im lặng bỏ qua chứ đừng ném lỗi ra console.
    if (!('serviceWorker' in navigator)) return;
    if (location.protocol !== 'https:' && location.hostname !== 'localhost' && location.hostname !== '127.0.0.1') return;

    window.addEventListener('load', async () => {
        try {
            const reg = await navigator.serviceWorker.register('/sw.js', { scope: '/' });

            // Có bản mới đang chờ: báo cho người dùng thay vì tự tải lại giữa lúc họ đang làm bài.
            reg.addEventListener('updatefound', () => {
                const sw = reg.installing;
                if (!sw) return;
                sw.addEventListener('statechange', () => {
                    // controller khác null nghĩa là đã từng có bản cũ chạy — đây là CẬP NHẬT, không phải
                    // lần cài đầu tiên (lần đầu thì không có gì để báo).
                    if (sw.state === 'installed' && navigator.serviceWorker.controller) showUpdateBar(reg);
                });
            });
        } catch { /* Không đăng ký được thì trang vẫn chạy bình thường, chỉ mất phần ngoại tuyến. */ }
    });

    // Sau khi bản mới nắm quyền thì tải lại đúng MỘT lần. Cờ chặn để tránh vòng lặp tải lại vô tận
    // khi có nhiều tab cùng mở.
    let reloading = false;
    navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (reloading) return;
        reloading = true;
        location.reload();
    });

    function showUpdateBar(reg) {
        if (document.getElementById('pwaUpdateBar')) return;
        const bar = document.createElement('div');
        bar.id = 'pwaUpdateBar';
        bar.className = 'pwa-toast';
        bar.innerHTML = '<span>Đã có phiên bản mới.</span>'
            + '<button type="button" class="pwa-toast-main">Tải lại</button>'
            + '<button type="button" class="pwa-toast-x" aria-label="Đóng">&times;</button>';
        bar.querySelector('.pwa-toast-main').onclick = () => reg.waiting?.postMessage('skip-waiting');
        bar.querySelector('.pwa-toast-x').onclick = () => bar.remove();
        document.body.appendChild(bar);
    }

    // ---------- Nút "Cài ứng dụng" ----------
    // Android/Chrome bắn beforeinstallprompt; giữ lại sự kiện để hiện nút của mình đúng lúc người dùng
    // muốn, thay vì để trình duyệt tự nhắc rồi họ bấm nhầm nút Đóng và mất luôn cơ hội.
    // iOS/Safari KHÔNG có sự kiện này — ở đó chỉ có thể hướng dẫn bấm Chia sẻ › Thêm vào MH chính.
    let installEvent = null;
    window.addEventListener('beforeinstallprompt', e => {
        e.preventDefault();
        installEvent = e;
        document.dispatchEvent(new CustomEvent('pwa:installable'));
    });

    /** Gọi từ nút "Cài ứng dụng". Trả về true nếu người dùng đồng ý cài. */
    window.pwaInstall = async function () {
        if (!installEvent) return false;
        installEvent.prompt();
        const { outcome } = await installEvent.userChoice;
        installEvent = null;   // Sự kiện chỉ dùng được một lần.
        return outcome === 'accepted';
    };

    /** Có nút cài để hiện không (đã cài rồi hoặc trình duyệt không hỗ trợ thì false). */
    window.pwaCanInstall = () => !!installEvent;

    /** Đang chạy ở dạng app đã cài (không phải trong tab trình duyệt). */
    window.pwaIsStandalone = () =>
        window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
})();
