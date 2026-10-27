// File: wwwroot/sw.js
// Service worker cho PWA. Nhiệm vụ DUY NHẤT là làm giao diện mở nhanh và còn mở được khi rớt mạng —
// KHÔNG đụng tới dữ liệu. Mọi thứ dưới /api, /hubs, /uploads đều đi thẳng ra mạng, không qua cache:
// dữ liệu học tập là dữ liệu cá nhân và thay đổi liên tục, cache nhầm là hiện điểm/khóa học của phiên
// đăng nhập trước cho người đăng nhập sau trên cùng máy.

// ĐỔI SỐ NÀY mỗi lần sửa danh sách SHELL hoặc đổi chiến lược cache — tên cache đổi thì bản cũ bị xóa
// sạch ở bước activate, người dùng không kẹt lại ở phiên bản cũ.
const VERSION = 'v47';
const SHELL_CACHE = `hbe-shell-${VERSION}`;
const PAGE_CACHE = `hbe-page-${VERSION}`;

// Tài nguyên khung: nạp sẵn ngay lúc cài để lần mở thứ hai không cần mạng.
// Cố ý KHÔNG liệt kê hết mọi trang — trang nào người dùng đã mở thì tự vào PAGE_CACHE ở dưới.
const SHELL = [
    '/css/style.css',
    '/js/api.js',
    '/js/app.js',
    '/js/language.js',
    '/js/theme.js',
    '/locales/vi.json',
    '/locales/en.json',
    '/img/HB.png',
    '/img/icons/icon-192.png',
    '/manifest.webmanifest',
];

// Đường dẫn không bao giờ được cache. /scorm-content là nội dung các gói SCORM — cả một website mỗi gói, cache
// vào đây là phình bộ nhớ của trình duyệt theo mọi gói từng mở.
const NEVER_CACHE = ['/api/', '/hubs/', '/uploads/', '/swagger', '/scorm-content/'];

self.addEventListener('install', event => {
    event.waitUntil((async () => {
        const cache = await caches.open(SHELL_CACHE);
        // addAll() thất bại toàn bộ nếu một file lỗi 404 — nạp từng file rời để một tệp thiếu
        // không làm hỏng cả lần cài đặt.
        await Promise.all(SHELL.map(url => cache.add(url).catch(() => { })));
    })());
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        const names = await caches.keys();
        await Promise.all(
            names.filter(n => n.startsWith('hbe-') && n !== SHELL_CACHE && n !== PAGE_CACHE)
                 .map(n => caches.delete(n))
        );
        await self.clients.claim();
    })());
});

// Trang gọi tới khi người dùng bấm "Tải lại" ở thông báo có bản mới.
self.addEventListener('message', event => {
    if (event.data === 'skip-waiting') self.skipWaiting();
});

self.addEventListener('fetch', event => {
    const req = event.request;

    // Chỉ xử lý GET cùng origin. POST/PUT/DELETE và tài nguyên bên thứ ba (Google Fonts, CDN)
    // để trình duyệt tự lo.
    if (req.method !== 'GET') return;

    const url = new URL(req.url);
    if (url.origin !== self.location.origin) return;
    if (NEVER_CACHE.some(p => url.pathname.startsWith(p))) return;

    // Trang HTML: ưu tiên mạng để luôn thấy bản mới nhất, rớt mạng mới lấy bản đã lưu.
    if (req.mode === 'navigate' || req.destination === 'document') {
        event.respondWith((async () => {
            try {
                const res = await fetch(req);
                if (res.ok) (await caches.open(PAGE_CACHE)).put(req, res.clone());
                return res;
            } catch {
                return (await caches.match(req)) || (await caches.match('/pages/dashboard.html'))
                    || new Response(
                        '<meta charset="utf-8"><p style="font:15px system-ui;padding:24px">Đang ngoại tuyến — hãy mở lại khi có mạng.</p>',
                        { headers: { 'Content-Type': 'text/html; charset=utf-8' }, status: 503 }
                    );
            }
        })());
        return;
    }

    // JSON ngôn ngữ: cũng ưu tiên mạng. Để stale-while-revalidate thì sửa một câu dịch phải tải lại
    // trang HAI lần mới thấy — lần đầu vẫn ra chữ cũ trong cache.
    if (url.pathname.startsWith('/locales/')) {
        event.respondWith((async () => {
            try {
                const res = await fetch(req, { cache: 'no-cache' });
                if (res.ok) (await caches.open(SHELL_CACHE)).put(req, res.clone());
                return res;
            } catch {
                return (await caches.match(req)) || Response.error();
            }
        })());
        return;
    }

    // CSS/JS/ảnh/font: trả bản đã lưu ngay cho nhanh, đồng thời âm thầm tải bản mới
    // về cho lần sau (stale-while-revalidate).
    event.respondWith((async () => {
        const cached = await caches.match(req);
        const fresh = fetch(req).then(res => {
            if (res.ok) caches.open(SHELL_CACHE).then(c => c.put(req, res.clone()));
            return res;
        }).catch(() => null);
        return cached || (await fresh) || Response.error();
    })());
});
