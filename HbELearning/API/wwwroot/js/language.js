// File: wwwroot/js/language.js
// Chuyển đổi ngôn ngữ giao diện. Phải nạp TRƯỚC app.js vì app.js gọi t() lúc dựng menu.
//
// Bản dịch KHÔNG nằm trong file này nữa mà ở /locales/<mã>.json — giống cách .NET tách .resx
// ra khỏi mã nguồn. Thêm ngôn ngữ mới = thêm 1 file JSON + 1 dòng trong LOCALE_TAG/FLAG_SVG,
// không phải sửa code.
//
// Quy ước đặt khóa: "trang.thanhphan" (menu.dashboard, common.save...).
// KHÔNG dùng chính chữ tiếng Việt làm khóa — sửa chính tả một phát là rụng bản dịch.
//
// CẢNH BÁO: tên vai trò tiếng Việt ("Trưởng phòng", "Giám Đốc"...) đang được dùng làm KHÓA
// so sánh phân quyền (Auth.require, MENU[].role). Chỉ được dịch ở tầng hiển thị bằng
// roleLabel() bên dưới, tuyệt đối không đổi chuỗi gốc.

const LANG_KEY = 'hbe_lang';

/** Danh sách ngôn ngữ dùng khi CHƯA gọi được API (server đang khởi động lại, lỗi mạng) — đúng hai ngôn ngữ
 *  có sẵn file trong wwwroot/locales. Danh sách thật lấy từ bảng ngôn ngữ, xem loadLanguageList(). */
const FALLBACK_LANGUAGE_LIST = [
    { lang: 'vi', label: 'Tiếng Việt', localeTag: 'vi-VN' },
    { lang: 'en', label: 'English', localeTag: 'en-US' },
];

/** Các ngôn ngữ đang bật. Admin thêm ngôn ngữ mới ở màn hình "Dịch ngôn ngữ" là danh sách này dài ra, không
 *  phải sửa file nào. Rỗng cho tới khi Lang.ready hoàn tất. */
let LANGUAGE_LIST = FALLBACK_LANGUAGE_LIST;

/** Từ điển của ngôn ngữ đang dùng. Rỗng cho tới khi Lang.ready hoàn tất. */
let DICT = {};

/** Tải từ điển của một ngôn ngữ.
 *
 *  Nguồn CHÍNH là CSDL (/api/translation/dictionary/<mã>), vì đó là nơi chứa cả bản xuất xưởng lẫn thuật ngữ
 *  chuyên ngành công ty tự sửa ở màn hình "Dịch ngôn ngữ". File JSON trong /locales là bản gốc được nạp vào
 *  CSDL lúc khởi động, và ở đây đóng vai trò PHƯƠNG ÁN DỰ PHÒNG: mất API (server đang khởi động lại, lỗi
 *  mạng) thì giao diện vẫn còn chữ để hiện thay vì trơ ra chuỗi dự phòng trong HTML.
 *
 *  Endpoint từ điển không đòi đăng nhập — trang đăng nhập và trang chủ cũng cần dịch mà lúc đó chưa có token. */
async function loadDictionary(lang) {
    try {
        const response = await fetch(`/api/translation/dictionary/${lang}`, { cache: 'no-cache' });
        if (response.ok) {
            const dictionary = await response.json();
            // Bảng rỗng nghĩa là chưa nạp bản dịch (CSDL mới tinh) — rơi về file JSON thay vì để trang trắng chữ.
            if (dictionary && Object.keys(dictionary).length) return dictionary;
        }
    } catch {
        // Nuốt lỗi có chủ đích: đã có đường lui bên dưới, log ở đây chỉ làm console đỏ mỗi lần server khởi động lại.
    }
    return await loadDictionaryFile(lang);
}

/** Đọc thẳng file /locales/<mã>.json. Lỗi thì trả object rỗng — t() sẽ tự rơi về chữ gốc trong HTML. */
async function loadDictionaryFile(lang) {
    try {
        // no-cache: sửa file JSON là F5 thấy ngay, khỏi phải Ctrl+F5.
        const response = await fetch(`/locales/${lang}.json`, { cache: 'no-cache' });
        if (!response.ok) {
            console.error(`[i18n] Không tải được /locales/${lang}.json — HTTP ${response.status}`);
            return {};
        }
        return await response.json();
    } catch (ex) {
        // Thường là JSON sai cú pháp (thừa dấu phẩy cuối, thiếu ngoặc kép).
        console.error(`[i18n] Lỗi đọc /locales/${lang}.json:`, ex.message);
        return {};
    }
}

/** Các ngôn ngữ đang bật, đọc từ CSDL. Không đòi đăng nhập vì nút chuyển ngôn ngữ có mặt ở cả trang đăng
 *  nhập lẫn trang chủ. Lỗi thì dùng danh sách dự phòng để nút vẫn bấm được. */
async function loadLanguageList() {
    try {
        const response = await fetch('/api/translation/language', { cache: 'no-cache' });
        if (!response.ok) return FALLBACK_LANGUAGE_LIST;
        const list = (await response.json()).filter(x => x.isActive);
        return list.length ? list : FALLBACK_LANGUAGE_LIST;
    } catch {
        return FALLBACK_LANGUAGE_LIST;
    }
}

/** Bắt đầu tải ngay lúc file này được nạp, song song với các script khác.
 *
 *  Gọi SONG SONG danh sách ngôn ngữ và từ điển thay vì nối tiếp: chờ xong cái này mới gọi cái kia là cộng
 *  thêm một vòng mạng vào MỌI lần tải trang, chỉ để phục vụ trường hợp hiếm là ngôn ngữ đang chọn vừa bị xóa. */
const _dictionaryReady = (async () => {
    const storedLang = localStorage.getItem(LANG_KEY) || 'vi';
    const [languageList, dictionary] = await Promise.all([loadLanguageList(), loadDictionary(storedLang)]);

    LANGUAGE_LIST = languageList;
    DICT = dictionary;

    // Ngôn ngữ đang chọn không còn nữa (Admin đã xóa hoặc tắt nó) -> quay về ngôn ngữ đầu danh sách. Không
    // xử lý thì người dùng kẹt ở một giao diện trơ chữ dự phòng mà không hiểu vì sao.
    if (!LANGUAGE_LIST.some(x => x.lang === storedLang)) {
        const fallbackLang = LANGUAGE_LIST[0].lang;
        localStorage.setItem(LANG_KEY, fallbackLang);
        DICT = await loadDictionary(fallbackLang);
    }

    return DICT;
})();

/** Cờ quốc gia vẽ bằng SVG nội tuyến — KHÔNG dùng emoji 🇻🇳🇬🇧 vì Windows không có font vẽ cờ,
 *  emoji cờ thực chất là cặp ký tự Regional Indicator nên máy Windows hiện ra chữ "VN"/"GB". */
const FLAG_SVG = {
    vi: `<svg class="flag" viewBox="0 0 30 20" aria-hidden="true">
            <rect width="30" height="20" fill="#da251d"/>
            <polygon fill="#ff0" points="15,4 16.35,8.15 20.71,8.15 17.18,10.71 18.53,14.85 15,12.29 11.47,14.85 12.82,10.71 9.29,8.15 13.65,8.15"/>
         </svg>`,
    en: `<svg class="flag" viewBox="0 0 30 20" aria-hidden="true">
            <rect width="30" height="20" fill="#012169"/>
            <path d="M0,0 L30,20 M30,0 L0,20" stroke="#fff" stroke-width="4"/>
            <path d="M0,0 L30,20 M30,0 L0,20" stroke="#c8102e" stroke-width="2"/>
            <path d="M15,0 V20 M0,10 H30" stroke="#fff" stroke-width="6"/>
            <path d="M15,0 V20 M0,10 H30" stroke="#c8102e" stroke-width="3.6"/>
         </svg>`
};

/** Cờ cho ngôn ngữ công ty tự thêm: không thể vẽ sẵn cờ cho mọi nước, nên dùng một quả địa cầu trung tính
 *  thay vì bỏ trống — nút chỉ còn mỗi hai chữ cái nhìn như bị lỗi hiển thị. */
const GLOBE_SVG = `<svg class="flag" viewBox="0 0 30 20" aria-hidden="true">
        <rect width="30" height="20" fill="#e2e8f0"/>
        <g fill="none" stroke="#475569" stroke-width="1.1">
            <circle cx="15" cy="10" r="6.4"/>
            <ellipse cx="15" cy="10" rx="2.6" ry="6.4"/>
            <path d="M8.6 10h12.8M9.8 6.4h10.4M9.8 13.6h10.4"/>
        </g>
     </svg>`;

/** Lá cờ của một ngôn ngữ, dùng nhúng thẳng vào template literal. */
function flagIcon(lang) { return FLAG_SVG[lang] || GLOBE_SVG; }

const Lang = {
    /** Mã ngôn ngữ đang dùng ('vi', 'en', hoặc ngôn ngữ công ty tự thêm). */
    get current() { return localStorage.getItem(LANG_KEY) || 'vi'; },

    /** Các ngôn ngữ đang bật, theo đúng thứ tự hiển thị. */
    get list() { return LANGUAGE_LIST; },

    /** Tên hiển thị của ngôn ngữ đang dùng. */
    get label() { return LANGUAGE_LIST.find(x => x.lang === this.current)?.label || this.current.toUpperCase(); },

    /** Thẻ locale để định dạng ngày/giờ/số — dùng thay cho 'vi-VN' hardcode. */
    get locale() { return LANGUAGE_LIST.find(x => x.lang === this.current)?.localeTag || 'vi-VN'; },

    /** Từ điển đã tải xong chưa. Mỗi trang phải "await Lang.ready" TRƯỚC khi dựng giao diện,
     *  vì fetch là bất đồng bộ còn t() thì đồng bộ. Quên await thì trang vẫn chạy nhưng
     *  hiện chữ tiếng Việt gốc thay vì bản dịch. */
    get ready() { return _dictionaryReady; },

    /** Đổi ngôn ngữ. Tải lại trang vì phần lớn chữ được sinh ra trong template literal
     *  lúc load() chạy, vẽ lại tại chỗ sẽ không quét tới. */
    set(lang) {
        if (lang === this.current || !LANGUAGE_LIST.some(x => x.lang === lang)) return;
        localStorage.setItem(LANG_KEY, lang);
        location.reload();
    },
};


// ==================== NÚT CHỌN NGÔN NGỮ ====================
// Trước đây nút này lật qua lại giữa hai ngôn ngữ, rồi thành xoay vòng khi có ngôn ngữ thứ ba. Xoay vòng chỉ
// chấp nhận được tới ba: từ bốn ngôn ngữ trở đi, muốn về đúng tiếng của mình phải bấm mò vài lần, mà mỗi lần
// bấm là một lần tải lại trang. Nên giờ là một danh sách xổ xuống — nhìn thấy hết, bấm một phát tới đúng nơi.
//
// Đặt ở đây (language.js) chứ không ở app.js vì trang đăng nhập KHÔNG nạp app.js, mà nút đổi ngôn ngữ thì có
// mặt ở cả ba nơi: thanh trên cùng, trang đăng nhập và trang chủ.

/** Bảng kiểu của danh sách xổ xuống, tự chèn một lần.
 *
 *  Không viết vào css/style.css vì trang đăng nhập chỉ nạp css/login.css — để ở một file thì trang kia mất
 *  kiểu, để ở cả hai thì sửa một chỗ quên chỗ kia. Danh sách là một thẻ nổi màu trắng nên hợp cả nền sáng của
 *  thanh trên lẫn nền tối của trang đăng nhập. */
function ensureLangMenuStyle() {
    if (document.getElementById('lang-menu-style')) return;

    const style = document.createElement('style');
    style.id = 'lang-menu-style';
    style.textContent = `
        .lang-caret { font-size: 11px; opacity: .65; }
        .lang-menu {
            position: fixed; z-index: 4000; min-width: 200px; max-height: min(60vh, 420px); overflow-y: auto;
            padding: 5px; border-radius: 10px; border: 1px solid #e2e8f0; background: #fff;
            box-shadow: 0 12px 32px rgba(15, 23, 42, .18);
            font-family: inherit; font-size: 13px; color: #0f172a;
        }
        .lang-menu button {
            display: flex; align-items: center; gap: 9px; width: 100%;
            padding: 8px 10px; border: none; border-radius: 7px; background: none;
            font: inherit; color: inherit; text-align: left; cursor: pointer;
        }
        .lang-menu button:hover { background: #f1f5f9; }
        .lang-menu button.current { font-weight: 700; }
        .lang-menu .flag {
            flex: none; display: block; width: 22px; height: 15px; border-radius: 2px;
            box-shadow: 0 0 0 1px rgba(15, 23, 42, .15);
        }
        .lang-menu .lang-name { flex: 1; }
        .lang-menu .lang-code { font-size: 11px; color: #64748b; letter-spacing: .4px; }
        .lang-menu .lang-check { color: #0284c7; }

        /* Bản màu tối. Danh sách này tự chèn kiểu bằng màu cứng (xem ghi chú ở trên) nên không dùng được
           biến của style.css — phải viết lại đủ bộ ở đây. */
        :root[data-theme="dark"] .lang-menu {
            border-color: #26324a; background: #131c2e; color: #e8edf6;
            box-shadow: 0 12px 32px rgba(0, 0, 0, .55);
        }
        :root[data-theme="dark"] .lang-menu button:hover { background: #1e293b; }
        :root[data-theme="dark"] .lang-menu .lang-code { color: #93a3bd; }
        :root[data-theme="dark"] .lang-menu .lang-check { color: #38bdf8; }
        :root[data-theme="dark"] .lang-menu .flag { box-shadow: 0 0 0 1px rgba(226, 232, 240, .25); }`;
    document.head.appendChild(style);
}

/** Danh sách đang mở, nếu có. Chỉ cho phép MỘT cái tồn tại: mở cái thứ hai mà không dọn cái cũ thì hai thẻ
 *  chồng lên nhau và cái bị che vẫn bắt được cú bấm chuột. */
let langMenuEl = null;

function closeLangMenu() {
    langMenuEl?.remove();
    langMenuEl = null;
}

function openLangMenu(buttonEl) {
    ensureLangMenuStyle();
    closeLangMenu();
    // app.js chưa chắc đã nạp (trang đăng nhập cũng dùng nút này) nên gọi kiểu tùy chọn.
    if (typeof closeTopbarOverlay === 'function') closeTopbarOverlay('lang');

    const menu = document.createElement('div');
    menu.className = 'lang-menu';
    menu.setAttribute('role', 'listbox');
    menu.innerHTML = Lang.list.map(item => `
        <button type="button" role="option" data-lang="${item.lang}"
                aria-selected="${item.lang === Lang.current}"
                class="${item.lang === Lang.current ? 'current' : ''}">
            ${flagIcon(item.lang)}
            <span class="lang-name">${item.label}</span>
            <span class="lang-code">${item.lang.toUpperCase()}</span>
            ${item.lang === Lang.current ? '<span class="lang-check">✓</span>' : ''}
        </button>`).join('');

    document.body.appendChild(menu);
    langMenuEl = menu;

    // Gắn vào body rồi định vị bằng tọa độ màn hình, thay vì đặt bên trong thanh trên: thanh trên có vùng
    // cuộn và ngữ cảnh xếp lớp riêng, thả danh sách vào đó là nó bị cắt cụt hoặc chui xuống dưới nội dung.
    const rect = buttonEl.getBoundingClientRect();
    const width = menu.offsetWidth;
    const left = Math.min(Math.max(8, rect.right - width), window.innerWidth - width - 8);
    menu.style.left = `${left}px`;
    menu.style.top = `${rect.bottom + 6}px`;

    menu.querySelectorAll('[data-lang]').forEach(el =>
        el.addEventListener('click', () => { closeLangMenu(); Lang.set(el.dataset.lang); }));
}

// Đóng danh sách khi bấm ra ngoài, bấm Esc, hoặc khi trang cuộn / đổi kích thước — lúc đó tọa độ đã tính
// không còn đúng nữa, để nguyên thì danh sách trôi lơ lửng giữa màn hình.
document.addEventListener('click', () => closeLangMenu());
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeLangMenu(); });
window.addEventListener('resize', () => closeLangMenu());
window.addEventListener('scroll', () => closeLangMenu(), true);

/**
 * Dựng nút chọn ngôn ngữ vào một phần tử <button> có sẵn. Nút giữ nguyên class của trang đang dùng
 * (.lang-switch hoặc .lang-switch-login) để hợp với thiết kế từng trang; hàm này chỉ lo phần bên trong và
 * hành vi xổ xuống.
 *
 * Gọi được nhiều lần trên cùng một nút — dùng onclick chứ không addEventListener, để lần gọi sau thay hẳn
 * lần trước thay vì cộng dồn handler.
 */
function renderLanguageSwitch(buttonEl) {
    if (!buttonEl) return;

    buttonEl.innerHTML = `${flagIcon(Lang.current)}<span>${Lang.current.toUpperCase()}</span>`
        + (Lang.list.length > 1 ? '<i class="ph ph-caret-down lang-caret"></i>' : '');
    buttonEl.title = `${t('shell.switchLang', 'Chuyển ngôn ngữ')} — ${Lang.label}`;

    // Chỉ có đúng một ngôn ngữ thì không có gì để chọn: xổ ra một danh sách một dòng chỉ làm người dùng
    // tưởng hệ thống lỗi.
    if (Lang.list.length < 2) { buttonEl.onclick = null; return; }

    buttonEl.setAttribute('aria-haspopup', 'listbox');
    buttonEl.onclick = (e) => {
        e.stopPropagation();   // không chặn thì chính cú bấm này lọt xuống document và đóng ngay danh sách vừa mở
        if (langMenuEl) closeLangMenu(); else openLangMenu(buttonEl);
    };
}

/** Dịch một khóa. Thiếu bản dịch thì trả fallback (chữ tiếng Việt gốc) — không bao giờ lòi khóa trần ra màn hình.
 *  Tham số thứ 3 để thay chỗ trống dạng {tên}:  t('x', 'Tổng {count} tài khoản', { count: 12 }) */
function t(key, fallback, params) {
    let text = DICT[key] ?? fallback ?? key;
    if (params)
        for (const [name, value] of Object.entries(params))
            text = text.replaceAll(`{${name}}`, value);
    return text;
}

/** Dịch tên vai trò để hiển thị. Chuỗi gốc vẫn giữ nguyên cho mọi phép so sánh phân quyền. */
function roleLabel(roleName) {
    return t(`role.${roleName}`, roleName);
}

/** Quét DOM thay chữ theo thuộc tính data-i18n*. Gọi lại được nhiều lần, gọi cả trên DOM vừa render.
 *  Lưu ý: dùng textContent nên chỉ đặt data-i18n lên thẻ chứa THUẦN chữ — có icon bên trong
 *  thì bọc riêng phần chữ vào <span data-i18n="...">, nếu không icon bị xóa mất. */
function applyI18n(root = document) {
    document.documentElement.lang = Lang.current;
    translateTree(root);
}

/** Dịch một nhánh DOM (dùng lại cho cả trang lẫn từng khối vừa được render). */
function translateTree(root) {
    root.querySelectorAll('[data-i18n]').forEach(el => el.textContent = t(el.dataset.i18n, el.textContent));
    root.querySelectorAll('[data-i18n-ph]').forEach(el => el.placeholder = t(el.dataset.i18nPh, el.placeholder));
    root.querySelectorAll('[data-i18n-title]').forEach(el => el.title = t(el.dataset.i18nTitle, el.title));
    // content: dùng cho <meta name="description"> — Google đọc thẻ này để hiện mô tả trên kết quả tìm kiếm.
    root.querySelectorAll('[data-i18n-alt]').forEach(el => el.alt = t(el.dataset.i18nAlt, el.alt));
    root.querySelectorAll('[data-i18n-content]').forEach(el => el.content = t(el.dataset.i18nContent, el.content));

    // querySelectorAll chỉ tìm con cháu — nếu chính phần tử gốc mang data-i18n thì phải xử lý riêng.
    if (root.nodeType === 1 && root.dataset) {
        if (root.dataset.i18n) root.textContent = t(root.dataset.i18n, root.textContent);
        if (root.dataset.i18nPh) root.placeholder = t(root.dataset.i18nPh, root.placeholder);
        if (root.dataset.i18nTitle) root.title = t(root.dataset.i18nTitle, root.title);
        if (root.dataset.i18nAlt) root.alt = t(root.dataset.i18nAlt, root.alt);
    }
}

/**
 * Tự dịch cả những khối HTML được JavaScript render sau (bảng, thẻ, modal...).
 *
 * Vì sao cần: 3/4 số chữ trong dự án nằm trong template literal của từng trang, được gán vào
 * innerHTML SAU khi applyI18n() chạy. Nếu không có bộ theo dõi này thì mỗi chỗ render xong
 * đều phải nhớ gọi applyI18n() lại — quên một chỗ là sót chữ, rất khó phát hiện.
 *
 * Nhờ vậy, dịch một khối HTML trong template literal chỉ cần gắn data-i18n như HTML tĩnh.
 */
function observeI18n() {
    new MutationObserver(recordList => {
        for (const record of recordList)
            for (const node of record.addedNodes)
                // Bỏ qua text node (nodeType 3) — chính textContent do applyI18n ghi ra,
                // xử lý tiếp sẽ thành vòng lặp vô tận.
                if (node.nodeType === 1) translateTree(node);
    }).observe(document.documentElement, { childList: true, subtree: true });
}

observeI18n();
