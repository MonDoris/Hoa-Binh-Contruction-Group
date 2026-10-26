// File: wwwroot/js/emoji.js
// ============================================================================================
// EMOJI THAY ICON TRANG TRÍ
//
// Quy ước giao diện: khung điều hướng và các nút thao tác dùng Icon (Phosphor); phần còn lại — thẻ thống kê, trạng
// thái rỗng, huy hiệu, tiêu đề thẻ, danh sách — dùng emoji Fluent Emoji Color của Microsoft (MIT, xem /emoji/LICENSE),
// tự host trong /emoji nên chạy được cả khi không có Internet.
//
// Cách làm: GIỮ NGUYÊN thẻ <i class="ph ph-xxx"> trong mã trang, chỉ vẽ ảnh emoji vào bên trong nó và giấu chữ
// glyph của Phosphor bằng CSS. Nhờ vậy mã trang nào còn cầm tham chiếu tới <i> để đổi class (bật/tắt, đổi biểu tượng)
// vẫn chạy — bộ quan sát bên dưới vẽ lại khi class đổi, và trang dựng HTML động cũng được xử lý ngay khi chèn vào DOM.
//
// Muốn GIỮ icon ở một chỗ cụ thể: đặt data-keep-icon lên thẻ <i> hoặc lên phần tử cha. Ngược lại, data-force-emoji
// lên thẻ <i> bắt buộc dùng emoji dù nó nằm trong vùng điều hướng / nút. Muốn thêm / đổi emoji cho một
// icon: sửa MAP bên dưới (khóa = tên Phosphor bỏ "ph-", giá trị = tên tệp trong /emoji, không có .svg).
// ============================================================================================
(function () {
    'use strict';

    const MAP = {
        'alarm': 'alarm-clock',
        'archive': 'file-cabinet',
        'article': 'newspaper',
        'bell': 'bell',
        'bell-ringing': 'bell',
        'bell-slash': 'bell-with-slash',
        'book-bookmark': 'bookmark',
        'book-open': 'open-book',
        'book-open-text': 'open-book',
        'bookmark-simple': 'bookmark',
        'books': 'books',
        'brain': 'brain',
        'briefcase': 'briefcase',
        'broadcast': 'satellite-antenna',
        'broom': 'broom',
        'browsers': 'desktop-computer',
        'buildings': 'office-building',
        'calendar': 'calendar',
        'calendar-blank': 'calendar',
        'calendar-check': 'calendar',
        'calendar-dots': 'calendar',
        'calendar-plus': 'calendar',
        'camera': 'camera',
        'certificate': 'scroll',
        'chalkboard-teacher': 'school',
        'chart-bar': 'bar-chart',
        'chart-donut': 'bar-chart',
        'chart-line-up': 'chart-increasing',
        'chat-circle-dots': 'speech-balloon',
        'chat-circle-text': 'speech-balloon',
        'chat-text': 'speech-balloon',
        'chats-circle': 'speech-balloon',
        'check-circle': 'check-mark-button',
        'clipboard-text': 'clipboard',
        'clock': 'mantelpiece-clock',
        'clock-counter-clockwise': 'scroll',
        'cloud-arrow-up': 'cloud',
        'cloud-check': 'cloud',
        'cloud-slash': 'cloud',
        'confetti': 'party-popper',
        'crown': 'crown',
        'crown-simple': 'crown',
        'cube': 'game-die',
        'cursor-click': 'computer-mouse',
        'database': 'file-cabinet',
        'device-mobile': 'mobile-phone',
        'diamond': 'hb-mascot.png',  // điểm thưởng: linh vật HB Hòa Bình (ảnh riêng của công ty, không thuộc bộ Fluent)
        'envelope-simple': 'envelope',
        'exam': 'memo',
        'file': 'page-facing-up',
        'file-csv': 'page-facing-up',
        'file-doc': 'page-facing-up',
        'file-pdf': 'page-with-curl',
        'file-text': 'page-facing-up',
        'fire': 'fire',
        'flag-checkered': 'chequered-flag',
        'folder': 'file-folder',
        'folder-open': 'open-file-folder',
        'folder-simple': 'file-folder',
        'gear': 'gear',
        'gear-six': 'gear',
        'gift': 'wrapped-gift',
        'git-merge': 'chequered-flag',
        'globe': 'globe-showing-europe-africa',
        'globe-hemisphere-east': 'globe-showing-asia-australia',
        'globe-hemisphere-west': 'globe-showing-americas',
        'graduation-cap': 'graduation-cap',
        'hand-coins': 'coin',
        'hand-pointing': 'backhand-index-pointing-right',
        'hourglass': 'hourglass-not-done',
        'hourglass-medium': 'hourglass-not-done',
        'image': 'framed-picture',
        'image-square': 'framed-picture',
        'info': 'information',
        'keyhole': 'key',
        'lightbulb': 'light-bulb',
        'lightning': 'high-voltage',
        'lock': 'locked',
        'lock-key': 'locked-with-key',
        'lock-simple': 'locked',
        'map-pin': 'round-pushpin',
        'medal': '1st-place-medal',
        'medal-military': 'military-medal',
        'megaphone': 'megaphone',
        'microsoft-excel-logo': 'green-book',
        'microsoft-powerpoint-logo': 'orange-book',
        'microsoft-word-logo': 'blue-book',
        'monitor-arrow-up': 'desktop-computer',
        'moon': 'crescent-moon',
        'package': 'package',
        'paint-brush': 'paintbrush',
        'panorama': 'framed-picture',
        'person': 'bust-in-silhouette',
        'piggy-bank': 'money-bag',
        'play-circle': 'play-button',
        'plugs': 'electric-plug',
        'prohibit': 'prohibited',
        'question': 'red-question-mark',
        'receipt': 'receipt',
        'robot': 'robot',
        'seal-check': 'sports-medal',
        'seal-question': 'red-question-mark',
        'shield-check': 'shield',
        'shield-warning': 'warning',
        'smiley': 'grinning-face',
        'sparkle': 'sparkles',
        'star': 'star',
        'storefront': 'convenience-store',
        'sun': 'sun',
        'tag': 'label',
        'target': 'bullseye',
        'ticket': 'ticket',
        'timer': 'stopwatch',
        'translate': 'globe-showing-europe-africa',
        'tray': 'inbox-tray',
        'trend-up': 'chart-increasing',
        'trophy': 'trophy',
        'user': 'bust-in-silhouette',
        'user-circle': 'bust-in-silhouette',
        'user-minus': 'bust-in-silhouette',
        'user-plus': 'bust-in-silhouette',
        'user-switch': 'bust-in-silhouette',
        'users': 'busts-in-silhouette',
        'users-three': 'busts-in-silhouette',
        'warning': 'warning',
        'warning-circle': 'warning',
        'warning-octagon': 'stop-sign',
        'x-circle': 'cross-mark'
    };

    // Icon nằm trong các vùng này luôn giữ nguyên: điều hướng chính và mọi nút bấm.
    const KEEP_CONTEXT = [
        '.sidebar', '.topbar', 'nav', '.tabs', '[role="tab"]', '[role="button"]', 'button', '.btn', '.pop-menu', '.close-x',
        '.pagination', '.mobile-menu-btn', 'summary', 'th', '.ql-toolbar', '.fb-stars', '[data-keep-icon]',
    ].join(',');

    const VARIANT = new Set(['ph-fill', 'ph-bold', 'ph-duotone', 'ph-light', 'ph-thin']);

    function slugOf(el) {
        for (const cls of el.classList) {
            if (cls.startsWith('ph-') && !VARIANT.has(cls)) return MAP[cls.slice(3)] || null;
        }
        return null;
    }

    /** Vẽ (hoặc gỡ) emoji trong một thẻ <i>. Idempotent: gọi lại nhiều lần không sinh thay đổi DOM thừa. */
    function apply(el) {
        // data-force-emoji: dùng emoji dù nằm trong vùng giữ icon — để cùng một thứ (điểm thưởng, chuỗi ngày) hiện giống nhau ở mọi nơi.
        const slug = el.hasAttribute('data-force-emoji') || !el.closest(KEEP_CONTEXT) ? slugOf(el) : null;
        if (!slug) {
            if (el.dataset.emoji) {
                el.style.removeProperty('--hbe-emoji');
                el.classList.remove('hbe-emoji-host');
                delete el.dataset.emoji;
            }
            return;
        }
        if (el.dataset.emoji === slug) return;

        el.dataset.emoji = slug;
        el.classList.add('hbe-emoji-host');
        // QUAN TRỌNG: emoji được vẽ bằng ảnh NỀN của chính thẻ <i>, KHÔNG chèn thẻ <img> con. Stylesheet của các trang
        // có nhiều quy tắc "<vùng> img { ... }" dành cho ảnh thật (vd ".dash-thumb img { position:absolute; inset:0;
        // width:100%; height:100% }" cho ảnh bìa khóa học) — một thẻ <img> con rơi vào vùng đó sẽ bị kéo giãn phủ cả
        // vùng chứa. Ảnh nền thì không quy tắc <img> nào chạm tới được, nên lỗi đó không thể lặp lại ở trang khác.
        // Tên có đuôi (vd "hb-mascot.png") dùng nguyên; còn lại mặc định là .svg của bộ Fluent Emoji.
        el.style.setProperty('--hbe-emoji', `url("/emoji/${slug.includes('.') ? slug : slug + '.svg'}")`);
    }

    function scan(root) {
        if (root.nodeType !== 1) return;
        if (root.matches('i[class*="ph-"]')) apply(root);
        root.querySelectorAll('i[class*="ph-"]').forEach(apply);
    }

    const observer = new MutationObserver(records => {
        for (const record of records) {
            if (record.type === 'attributes') { if (record.target.tagName === 'I') apply(record.target); }
            else record.addedNodes.forEach(scan);
        }
    });

    function start() {
        const style = document.createElement('style');
        style.textContent = `
            .hbe-emoji-host { display: inline-flex; align-items: center; justify-content: center; width: 1.1em; height: 1.1em;
                font-style: normal; line-height: 1; vertical-align: -0.2em; flex-shrink: 0;
                background: var(--hbe-emoji) center / contain no-repeat; user-select: none; }
            .hbe-emoji-host::before,
            .hbe-emoji-host::after { display: none !important; }`;
        document.head.appendChild(style);

        scan(document.body);
        observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
    }

    if (document.body) start();
    else document.addEventListener('DOMContentLoaded', start);
})();
