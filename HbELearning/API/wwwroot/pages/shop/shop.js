// File: wwwroot/pages/shop/shop.js
(async function () {
    await Lang.ready;
    if (!Auth.require(['Admin', 'Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'], 'shop.view')) return;

    mountLayout('shop', 'Trung tâm điểm thưởng',
        'Đổi điểm tích lũy được từ việc học lấy quà của công ty');

    // Auth.can() trả null khi vai trò chưa cấu hình quyền; khi đó giữ cách chặn cũ theo tên vai trò.
    const canCreate = Auth.isAdmin && Auth.can('shop.create') !== false;
    const canUpdate = Auth.isAdmin && Auth.can('shop.update') !== false;
    const canDelete = Auth.isAdmin && Auth.can('shop.delete') !== false;
    const canManage = canCreate || canUpdate || canDelete;

    /** Ví điểm mới nhất — mọi màn hình trong trang đọc từ đây thay vì tự gọi lại API. */
    let wallet = { totalPoint: 0, usedPoint: 0, availablePoint: 0, pendingCount: 0 };
    let productList = [];
    /** Các đợt khuyến mãi ĐANG CHẠY — dùng cho biểu ngữ đầu tab Cửa hàng. Giá đã giảm thì server tính sẵn
     *  trong effectivePointCost của từng sản phẩm, ở đây không tính lại. */
    let runningPromotionList = [];
    let currentTab = 'store';

    const STATUS_BADGE = { Pending: 'orange', Delivered: 'green', Cancelled: 'gray' };
    const PROMO_STATUS_BADGE = { Running: 'green', Scheduled: 'blue', Ended: 'gray', Paused: 'orange' };
    const PROMO_COLOR_LIST = ['#dc2626', '#ea580c', '#d97706', '#16a34a', '#0891b2', '#2563eb', '#7c3aed', '#db2777'];

    /** Danh sách sản phẩm dùng cho ô tick trong biểu mẫu khuyến mãi — nạp khi mở tab, không phải mỗi lần mở form. */
    let promoProductList = [];
    const point = (value) => `<i class="ph-fill ph-diamond"></i> ${Number(value ?? 0).toLocaleString(Lang.locale)}`;
    const avatarHtml = (item) => item.userAvatarUrl
        ? `<img class="avatar" src="${esc(item.userAvatarUrl)}" alt="">`
        : `<div class="avatar">${esc(initial(item.userName))}</div>`;

    /** Bộ lọc của gian hàng. Giữ ngoài hàm vẽ: vẽ lại gian hàng (VD sau khi đổi quà) không được làm mất
     *  từ khóa và nhóm người dùng vừa chọn. */
    let storeKeyword = '';
    let storeCategory = '';

    /** Lượt đổi của chính mình + trạng thái đang lọc ở khu "Lượt đổi của tôi". Khai báo ở đây, TRƯỚC lượt
     *  await đầu tiên: openTab() chạy ngay sau await đó và đọc tới hai biến này. */
    let mineList = [];
    let mineStatus = '';

    if (canManage) document.getElementById('nav-admin').style.display = '';

    document.querySelectorAll('.shop-tab').forEach(button => {
        button.addEventListener('click', () => openTab(button.dataset.view));
    });

    document.getElementById('product-form').addEventListener('submit', saveProduct);
    document.getElementById('redeem-form').addEventListener('submit', submitRedeem);
    document.getElementById('handle-form').addEventListener('submit', submitHandle);
    document.getElementById('r-qty').addEventListener('input', renderRedeemCalc);
    document.getElementById('promo-form').addEventListener('submit', savePromotion);
    bindPromotionForm();

    await refreshWallet();
    refreshPendingBadge();
    // Thông báo đổi quà dẫn về đây kèm ?tab=mine — mở thẳng tab đó thay vì bắt người dùng tự tìm.
    await openTab(['store', 'mine', 'product', 'order', 'promo'].includes(qs('tab')) ? qs('tab') : 'store');

    async function openTab(tab) {
        if (!canManage && ['product', 'order', 'promo'].includes(tab)) tab = 'store';
        currentTab = tab;

        document.querySelectorAll('.shop-tab').forEach(b => {
            const on = b.dataset.view === tab;
            b.classList.toggle('active', on);
            if (on) b.setAttribute('aria-current', 'page'); else b.removeAttribute('aria-current');
        });
        document.querySelectorAll('.tab-panel').forEach(p => p.classList.toggle('active', p.id === `tab-${tab}`));

        // Ghi khu đang mở lên địa chỉ trang: bấm F5 hay gửi đường dẫn cho người khác vẫn mở đúng khu đó,
        // y như đường dẫn ?tab=mine trong thông báo đổi quà.
        const url = new URL(location.href);
        if (tab === 'store') url.searchParams.delete('tab'); else url.searchParams.set('tab', tab);
        history.replaceState(history.state, '', url);

        if (tab === 'store') await renderStore();
        else if (tab === 'mine') await renderMine();
        else if (tab === 'product') await renderProductManage();
        else if (tab === 'order') await renderOrder();
        else if (tab === 'promo') await renderPromotion();
    }

    /** Con số trên tab "Đơn đổi quà": bao nhiêu lượt đang chờ trao quà. Gọi riêng một lượt nhẹ
     *  thay vì chờ người quản trị mở đúng khu đó mới biết có việc — đó chính là thứ họ vào trang để làm. */
    async function refreshPendingBadge() {
        if (!canManage) return;
        try {
            const list = await API.getSilent('/shop/redemption?status=Pending');
            setNavNum('order', list.length, list.length > 0);
        } catch { /* Con số phụ trợ: hỏng thì để trống, không làm phiền bằng thông báo lỗi. */ }
    }

    /** Đặt con số bên phải một tab. count = 0 hoặc null thì giấu hẳn con số đi — một viên "0" không nói
     *  thêm điều gì mà vẫn hút mắt như thể có việc phải làm. */
    function setNavNum(view, count, warn = false) {
        const box = document.getElementById(`nav-num-${view}`);
        if (!box) return;
        box.textContent = count ? Number(count).toLocaleString(Lang.locale) : '';
        box.style.display = count ? '' : 'none';
        box.classList.toggle('warn', !!warn && !!count);
    }

    // ==================== VÍ ĐIỂM ====================
    /** Chỉ tải số liệu. Dải ví điểm nằm trong gian hàng và được vẽ lại mỗi lần vẽ gian hàng (sau khi đổi
     *  quà cũng vậy), nên ở đây không vẽ gì ngoài con số cạnh tab "Lượt đổi của tôi". */
    async function refreshWallet() {
        try { wallet = await API.get('/shop/me'); }
        catch (ex) { toastErr(ex.message); return; }

        setNavNum('mine', wallet.pendingCount, wallet.pendingCount > 0);
    }

    function fmtNum(value) {
        return Number(value ?? 0).toLocaleString(Lang.locale);
    }

    /** Dải ví điểm đầu gian hàng. Nói rõ ba con số là ba thứ khác nhau: viên kim cương trên thanh trên là
     *  điểm TÍCH LŨY (không bao giờ giảm, dùng cho xếp hạng), còn số to ở đây là phần còn tiêu được. */
    function walletHeroHtml() {
        const pending = wallet.pendingCount || 0;
        const goal = goalPanelHtml();

        return `
            <section class="wallet-hero ${goal ? '' : 'solo'}" aria-label="Ví điểm của bạn">
                <div>
                    <div class="wh-cap">Điểm khả dụng</div>
                    <div class="wh-balance">
                        <i class="ph-fill ph-diamond" aria-hidden="true"></i>
                        <b>${fmtNum(wallet.availablePoint)}</b><span>điểm</span>
                    </div>
                    <div class="wh-stats">
                        <span class="wh-stat" title="Con số trên viên kim cương ở thanh trên — đổi quà không làm nó giảm, nên thứ hạng của bạn không đổi">
                            <i class="ph ph-trend-up" aria-hidden="true"></i>Tích lũy <b>${fmtNum(wallet.totalPoint)}</b></span>
                        <span class="wh-stat">
                            <i class="ph ph-hand-coins" aria-hidden="true"></i>Đã dùng <b>${fmtNum(wallet.usedPoint)}</b></span>
                        <button type="button" class="wh-stat ${pending ? 'alert' : ''}" data-go-mine
                                title="Xem các lượt đổi của bạn">
                            <i class="ph ph-hourglass-medium" aria-hidden="true"></i>Chờ trao <b>${fmtNum(pending)}</b> lượt</button>
                    </div>
                </div>
                ${goal}
            </section>`;
    }

    /** Ô bên phải dải ví: món RẺ NHẤT còn hàng mà bạn chưa đủ điểm, kèm thanh tiến độ. Đủ điểm cho mọi món
     *  rồi thì đổi sang lời mời xem kệ "Đổi được ngay". Cửa hàng chưa có món nào còn hàng thì không vẽ. */
    function goalPanelHtml() {
        const inStock = productList.filter(x => x.stock > 0);
        if (!inStock.length) return '';

        const readyCount = inStock.filter(x => x.canAfford).length;
        const next = inStock.filter(x => !x.canAfford)
            .sort((a, b) => a.effectivePointCost - b.effectivePointCost)[0];

        if (!next) return `
            <div class="wh-goal">
                <div class="wh-cap"><i class="ph-fill ph-seal-check" aria-hidden="true"></i> Đổi được ngay</div>
                <div class="wh-goal-big">${fmtNum(readyCount)} món quà</div>
                <div class="wh-goal-sub">Bạn đủ điểm cho mọi món đang còn hàng.</div>
                <button type="button" class="wh-goal-btn" data-jump-ready>
                    Chọn quà <i class="ph ph-arrow-right" aria-hidden="true"></i></button>
            </div>`;

        const have = Math.max(0, wallet.availablePoint);
        const need = next.effectivePointCost;
        // Tối thiểu 4% để thanh không trống trơn khi mới có vài điểm — trống trơn đọc như "không có gì".
        const percent = Math.max(4, Math.min(100, Math.round(have / need * 100)));

        return `
            <div class="wh-goal">
                <div class="wh-cap"><i class="ph ph-target" aria-hidden="true"></i> Mục tiêu kế tiếp</div>
                <div class="wh-goal-item">
                    <span class="wh-thumb">${next.imageUrl
                        ? `<img src="${esc(next.imageUrl)}" alt="">`
                        : '<i class="ph-duotone ph-gift" aria-hidden="true"></i>'}</span>
                    <div style="min-width:0">
                        <div class="wh-goal-name" title="${esc(next.name)}">${esc(next.name)}</div>
                        <div class="wh-goal-sub">Còn thiếu <b>${fmtNum(need - have)}</b> điểm</div>
                    </div>
                </div>
                <div class="wh-meter" role="progressbar" aria-label="Tiến độ điểm tới ${esc(next.name)}"
                     aria-valuemin="0" aria-valuemax="${need}" aria-valuenow="${Math.min(have, need)}">
                    <span style="width:${percent}%"></span></div>
                <div class="wh-goal-foot">
                    <span>${fmtNum(have)} / ${fmtNum(need)} điểm</span>
                    <span>${readyCount
                        ? `Đổi được ngay: <b>${fmtNum(readyCount)}</b> món`
                        : 'Học bài, làm kiểm tra để tích thêm điểm'}</span>
                </div>
            </div>`;
    }

    // ==================== TAB: CỬA HÀNG ====================
    async function renderStore() {
        const box = document.getElementById('tab-store');
        box.innerHTML = `<div class="skel wallet-skel" aria-hidden="true"></div>` + skeletonHtml('cards', { count: 8 });

        try {
            // Hai lượt gọi song song: biểu ngữ và lưới hàng luôn phải nói cùng một chuyện, mà gọi nối tiếp
            // thì lưới đã vẽ xong rồi biểu ngữ mới nhảy vào đẩy nó xuống.
            [productList, runningPromotionList] = await Promise.all([
                API.get('/shop/product'),
                API.get('/shop/promotion'),
            ]);
        }
        catch (ex) { box.innerHTML = ''; toastErr(ex.message); return; }

        setNavNum('store', productList.length);

        box.innerHTML = `
            ${walletHeroHtml()}
            <div class="store-tools">
                <div class="store-search-row">
                    <div class="search-box">
                        <i class="ph ph-magnifying-glass" aria-hidden="true"></i>
                        <input id="s-keyword" type="search" maxlength="100" value="${esc(storeKeyword)}"
                               aria-label="Tìm quà" placeholder="Tìm quà theo tên, mô tả hoặc nhóm — VD: bình giữ nhiệt, voucher">
                    </div>
                    <span class="small muted nowrap" id="s-count" aria-live="polite"></span>
                </div>
                <div class="cat-row" id="cat-row" role="group" aria-label="Lọc theo nhóm sản phẩm"></div>
            </div>
            ${promotionBannerHtml()}
            <div id="store-grid"></div>`;

        box.querySelector('[data-go-mine]').addEventListener('click', () => {
            mineStatus = wallet.pendingCount > 0 ? 'Pending' : '';
            openTab('mine');
        });
        box.querySelector('[data-jump-ready]')?.addEventListener('click', () => {
            const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
            document.getElementById('shelf-ready')?.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'start' });
        });
        document.getElementById('s-keyword').addEventListener('input', e => {
            storeKeyword = e.target.value;
            drawStore();
        });
        drawCategoryChips();
        drawStore();
    }

    /** Hàng chip nhóm sản phẩm, kèm số món của từng nhóm. Nhóm là thứ người học bấm qua bấm lại để dạo
     *  cửa hàng, nên mỗi nhóm là một nút luôn nhìn thấy chứ không giấu trong ô xổ xuống. */
    function drawCategoryChips() {
        const box = document.getElementById('cat-row');
        if (!box) return;

        const countMap = new Map();
        productList.forEach(x => {
            if (!x.category) return;
            countMap.set(x.category, (countMap.get(x.category) || 0) + 1);
        });

        // Nhóm vừa chọn có thể đã biến mất (món cuối cùng của nhóm vừa bị xóa hoặc đổi nhóm) — bộ lọc trỏ
        // vào một nhóm không còn ai sẽ cho ra lưới rỗng mà không chip nào sáng lên để bấm bỏ.
        if (storeCategory && !countMap.has(storeCategory)) storeCategory = '';

        // Không có nhóm nào thì ẩn cả hàng — một hàng chỉ có đúng chip "Tất cả" không lọc được gì.
        box.style.display = countMap.size ? '' : 'none';
        if (!countMap.size) return;

        const chip = (value, label, count) => `
            <button type="button" class="chip ${storeCategory === value ? 'on' : ''}" data-category="${esc(value)}"
                    aria-pressed="${storeCategory === value}">${esc(label)} <span class="n">${count}</span></button>`;

        box.innerHTML = chip('', 'Tất cả', productList.length)
            + [...countMap.keys()].sort((a, b) => a.localeCompare(b, 'vi'))
                .map(name => chip(name, name, countMap.get(name))).join('');

        box.onclick = (e) => {
            const button = e.target.closest('[data-category]');
            if (button) pickCategory(button.dataset.category);
        };
    }

    function pickCategory(category) {
        storeCategory = category;
        drawCategoryChips();
        drawStore();
    }

    /** Lọc theo từ khóa + nhóm đang chọn rồi vẽ các kệ. */
    function drawStore() {
        const keyword = normalize(storeKeyword);
        const shown = productList.filter(x =>
            (!storeCategory || x.category === storeCategory)
            && (!keyword || normalize(`${x.name} ${x.description || ''} ${x.category || ''}`).includes(keyword)));

        const count = document.getElementById('s-count');
        if (count) count.textContent = shown.length === productList.length
            ? `${fmtNum(productList.length)} món quà`
            : `Hiện ${fmtNum(shown.length)}/${fmtNum(productList.length)} món quà`;

        drawStoreGrid(shown);
    }

    /** Biểu ngữ các đợt đang chạy, đặt trên cùng tab Cửa hàng. Không có đợt nào thì không chiếm chỗ. */
    function promotionBannerHtml() {
        if (!runningPromotionList.length) return '';
        return `<div class="promo-banner-list">` + runningPromotionList.map(x => `
            <div class="promo-banner" style="--promo-color:${esc(x.colorHex)}">
                <span class="ico"><i class="ph-fill ph-${esc(x.icon || 'confetti')}"></i></span>
                <div class="txt">
                    <b>${esc(x.name)}</b>
                    <div>${esc(x.scopeLabel)} · ${esc(remainLabel(x.endAt))}${x.description ? ' · ' + esc(x.description) : ''}</div>
                </div>
                <span class="cut">${esc(x.discountLabel)}</span>
            </div>`).join('') + `</div>`;
    }

    /** "Còn 3 ngày" / "Còn 5 giờ" / "Sắp kết thúc" — người học cần biết mình còn bao lâu để quyết định,
     *  chứ một mốc ngày giờ thì phải tự trừ ra mới hiểu. */
    function remainLabel(endAt) {
        const ms = new Date(endAt) - Date.now();
        if (ms <= 0) return 'Sắp kết thúc';
        const day = Math.floor(ms / 86400000);
        if (day >= 1) return `Còn ${day} ngày`;
        const hour = Math.floor(ms / 3600000);
        return hour >= 1 ? `Còn ${hour} giờ` : 'Sắp kết thúc';
    }

    /** Bỏ dấu tiếng Việt để gõ "binh giu nhiet" vẫn tìm ra "bình giữ nhiệt". */
    function normalize(text) {
        return String(text || '').toLowerCase().normalize('NFD')
            .replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd');
    }

    /** Chia các món đang hiện thành ba kệ — đổi được ngay / cần tích thêm / tạm hết — mỗi kệ xếp từ rẻ
     *  tới đắt. Kệ nào rỗng thì không vẽ. */
    function drawStoreGrid(list) {
        const box = document.getElementById('store-grid');
        if (!list.length) {
            const filtered = productList.length > 0;
            box.innerHTML = `<div class="card"><div class="card-body empty">
                <div class="big"><i class="ph ph-storefront"></i></div>
                <p>${filtered ? 'Không có món nào khớp bộ lọc.' : 'Chưa có sản phẩm nào trong cửa hàng.'}</p>
                ${filtered
                    ? '<button type="button" class="btn ghost mt" data-clear-filter><i class="ph ph-broom"></i> Xóa bộ lọc</button>'
                    : canCreate
                        ? '<button type="button" class="btn mt" data-go-product><i class="ph ph-plus"></i> Thêm món quà đầu tiên</button>'
                        : '<p class="small muted">Hãy quay lại sau — quản trị viên sẽ bổ sung quà để đổi.</p>'}
            </div></div>`;
            box.querySelector('[data-clear-filter]')?.addEventListener('click', () => {
                storeKeyword = '';
                document.getElementById('s-keyword').value = '';
                pickCategory('');
            });
            box.querySelector('[data-go-product]')?.addEventListener('click', async () => {
                await openTab('product');
                openProductForm();
            });
            return;
        }

        const byCost = (a, b) => a.effectivePointCost - b.effectivePointCost || a.name.localeCompare(b.name, 'vi');
        const shelfList = [
            { key: 'ready', icon: 'ph-check-circle', title: 'Đổi được ngay', note: '',
              items: list.filter(x => x.stock > 0 && x.canAfford) },
            { key: 'locked', icon: 'ph-lock-simple', title: 'Tích thêm điểm để đổi',
              items: list.filter(x => x.stock > 0 && !x.canAfford) },
            { key: 'out', icon: 'ph-package', title: 'Tạm hết hàng', note: 'Đổi được lại khi kho được bổ sung.',
              items: list.filter(x => x.stock <= 0) },
        ].filter(x => x.items.length);

        box.innerHTML = shelfList.map(shelf => `
            <section class="shelf shelf-${shelf.key}" id="shelf-${shelf.key}" aria-labelledby="shelf-${shelf.key}-title">
                <header class="shelf-head">
                    <h3 id="shelf-${shelf.key}-title"><i class="ph-fill ${shelf.icon}" aria-hidden="true"></i>${shelf.title}
                        <span class="num">${shelf.items.length}</span></h3>
                    ${shelf.note ? `<p>${shelf.note}</p>` : ''}
                </header>
                <div class="shop-grid">${shelf.items.sort(byCost).map(giftCardHtml).join('')}</div>
            </section>`).join('');

        box.querySelectorAll('[data-redeem]').forEach(b =>
            b.addEventListener('click', () => openRedeem(Number(b.dataset.redeem))));
        box.querySelectorAll('[data-cat]').forEach(b =>
            b.addEventListener('click', () => pickCategory(b.dataset.cat)));
    }

    /** Một thẻ quà. Nút luôn ghi rõ trạng thái bằng chữ + biểu tượng thay vì chỉ khóa lại và giấu lý do
     *  trong tooltip — trên điện thoại không có chuột để rê vào đọc tooltip. */
    function giftCardHtml(item) {
        const soldOut = item.stock <= 0;
        const short = !item.canAfford;
        const onSale = item.effectivePointCost < item.pointCost;
        const cost = item.effectivePointCost;
        const have = Math.max(0, wallet.availablePoint);

        // Hết hàng thì thanh tiến độ vô nghĩa (đủ điểm cũng không đổi được), nên bỏ hẳn.
        const meter = soldOut ? '' : short
            ? `<div class="shop-meter">
                   <div class="bar" aria-hidden="true"><span style="width:${Math.max(3, Math.round(have / cost * 100))}%"></span></div>
                   <div class="lab">Còn thiếu <b>${fmtNum(cost - have)}</b> điểm</div>
               </div>`
            : `<div class="shop-meter full">
                   <div class="bar" aria-hidden="true"><span style="width:100%"></span></div>
                   <div class="lab"><i class="ph-fill ph-check-circle" aria-hidden="true"></i> Đủ điểm · đổi xong còn ${fmtNum(have - cost)}</div>
               </div>`;

        const button = soldOut
            ? `<button type="button" class="btn locked" disabled><i class="ph ph-package"></i> Hết hàng</button>`
            : short
                ? `<button type="button" class="btn locked" disabled aria-label="Chưa đủ điểm để đổi ${esc(item.name)}">
                       <i class="ph ph-lock-simple"></i> Đổi quà</button>`
                : `<button type="button" class="btn" data-redeem="${item.id}" aria-label="Đổi ${esc(item.name)}">
                       <i class="ph ph-gift"></i> Đổi quà</button>`;

        return `<article class="shop-card ${soldOut ? 'off' : ''} ${onSale ? 'promo' : ''}"
                         style="${onSale ? `--promo-color:${esc(item.promotionColorHex)}` : ''}">
            <div class="shop-thumb">
                ${item.imageUrl ? `<img src="${esc(item.imageUrl)}" alt="${esc(item.name)}" loading="lazy">` : `<i class="ph-duotone ph-gift" aria-hidden="true"></i>`}
                ${onSale ? `<span class="shop-promo-flag" title="${esc(item.promotionName)}">
                    <i class="ph-fill ph-${esc(item.promotionIcon || 'confetti')}"></i> ${esc(item.discountLabel)}
                </span>` : ''}
                ${soldOut ? '<span class="shop-flag">Hết hàng</span>'
                    : item.stock <= 5 ? `<span class="shop-flag low">Chỉ còn ${item.stock}</span>` : ''}
            </div>
            <div class="shop-body">
                ${item.category ? `<button type="button" class="shop-cat" data-cat="${esc(item.category)}"
                        title="Xem các món nhóm ${esc(item.category)}"><i class="ph ph-tag" aria-hidden="true"></i><span>${esc(item.category)}</span></button>` : ''}
                <h4 title="${esc(item.name)}">${esc(item.name)}</h4>
                ${item.description ? `<p class="shop-desc">${esc(item.description)}</p>` : ''}
            </div>
            ${meter}
            <div class="shop-foot">
                <span class="shop-price"
                      title="${onSale ? esc(`Giá gốc ${item.pointCost} điểm — giảm còn ${cost} nhờ "${item.promotionName}"`) : ''}">
                    ${onSale ? `<span class="was">${fmtNum(item.pointCost)} điểm</span>` : ''}
                    <span class="now"><i class="ph-fill ph-diamond" aria-hidden="true"></i>${fmtNum(cost)}<small>điểm</small></span>
                </span>
                ${button}
            </div>
        </article>`;
    }

    // ==================== ĐỔI QUÀ ====================
    function openRedeem(productId) {
        const item = productList.find(x => x.id === productId);
        if (!item) return;

        document.getElementById('r-id').value = item.id;
        document.getElementById('r-product').innerHTML = `
            ${item.imageUrl ? `<img src="${esc(item.imageUrl)}" alt="">` : `<i class="ph ph-gift"></i>`}
            <div><b>${esc(item.name)}</b>
                <div class="small muted">${point(item.effectivePointCost)} / sản phẩm · còn ${item.stock} trong kho</div></div>`;

        // Trần số lượng lấy theo cái nào chặt hơn: kho còn bao nhiêu, hay giới hạn 20 mỗi lượt của server.
        const qtyInput = document.getElementById('r-qty');
        qtyInput.max = Math.min(20, item.stock);
        qtyInput.value = 1;

        renderRedeemCalc();
        openModal('modal-redeem');
    }

    function renderRedeemCalc() {
        const item = productList.find(x => x.id === Number(document.getElementById('r-id').value));
        if (!item) return;

        const qtyInput = document.getElementById('r-qty');
        const max = Math.min(20, item.stock);
        let quantity = Math.floor(Number(qtyInput.value) || 0);
        if (quantity < 1) quantity = 1;
        if (quantity > max) { quantity = max; qtyInput.value = max; }

        const total = item.effectivePointCost * quantity;
        const saved = (item.pointCost - item.effectivePointCost) * quantity;
        const left = wallet.availablePoint - total;
        const enough = left >= 0;

        // Dòng "được giảm" nằm giữa giá gốc và số tiền trừ, để phép tính đọc thành một câu liền mạch:
        // giá gốc bao nhiêu, giảm bao nhiêu, trừ bao nhiêu, còn lại bao nhiêu.
        document.getElementById('r-calc').innerHTML = `
            <div><span>Điểm khả dụng</span><span>${point(wallet.availablePoint)}</span></div>
            ${saved > 0 ? `
                <div><span>${quantity} × ${esc(item.name)} (giá gốc)</span>
                    <span>${item.pointCost * quantity}</span></div>
                <div style="color:var(--success)"><span>${esc(item.promotionName)} ${esc(item.discountLabel)}</span>
                    <span>− ${saved.toLocaleString(Lang.locale)}</span></div>
                <div><span>Thực trả</span><span>− ${total.toLocaleString(Lang.locale)}</span></div>`
            : `<div><span>${quantity} × ${esc(item.name)}</span>
                   <span>− ${total.toLocaleString(Lang.locale)}</span></div>`}
            <div class="${enough ? 'total' : 'bad'}">
                <span>${enough ? 'Còn lại sau khi đổi' : 'Thiếu'}</span>
                <span>${point(Math.abs(left))}</span>
            </div>`;

        document.getElementById('r-submit').disabled = !enough;
    }

    async function submitRedeem(event) {
        event.preventDefault();
        const id = Number(document.getElementById('r-id').value);
        const quantity = Math.floor(Number(document.getElementById('r-qty').value) || 1);

        try {
            const result = await API.post(`/shop/product/${id}/redeem`, { quantity });
            closeModal('modal-redeem');
            toastOk(result.message);
        } catch (ex) {
            toastErr(ex.message);
        }

        // Tải lại kể cả khi lỗi: lỗi thường gặp nhất là "vừa có người đổi mất" — lúc đó số liệu trên màn
        // hình đã cũ, và giữ nguyên nó thì người dùng bấm lại lần nữa cũng nhận đúng lời từ chối đó.
        await refreshWallet();
        await openTab(currentTab);
    }

    // ==================== TAB: LƯỢT ĐỔI CỦA TÔI ====================
    async function renderMine() {
        const box = document.getElementById('tab-mine');
        box.innerHTML = skeletonHtml('list', { count: 5, avatar: false });

        try { mineList = await API.get('/shop/my-redemption'); }
        catch (ex) { box.innerHTML = ''; toastErr(ex.message); return; }

        if (!mineList.length) {
            box.innerHTML = `<div class="card"><div class="card-body empty">
                <div class="big"><i class="ph ph-receipt"></i></div>
                <p>Bạn chưa đổi món quà nào.</p>
                <p class="small muted">Học thêm bài, làm bài kiểm tra và giữ chuỗi ngày học để tích điểm.</p>
                <button type="button" class="btn mt" data-go-store><i class="ph ph-storefront"></i> Xem gian hàng</button>
            </div></div>`;
            box.querySelector('[data-go-store]').addEventListener('click', () => openTab('store'));
            return;
        }

        // Lượt đã hủy được hoàn điểm rồi — cộng nó vào "điểm đã dùng" là đếm hai lần.
        const kept = mineList.filter(x => x.status !== 'Cancelled');
        const usedPoint = kept.reduce((sum, x) => sum + x.totalPoint, 0);
        const savedPoint = kept.reduce((sum, x) => sum + (x.savedPoint || 0), 0);

        box.innerHTML = `
            <div class="stat-strip">
                ${statTile('ph-receipt', 'var(--primary)', 'Lượt đã đổi', mineList.length)}
                ${statTile('ph-diamond', 'var(--info)', 'Điểm đã dùng', usedPoint)}
                ${statTile('ph-tag', 'var(--success)', 'Tiết kiệm nhờ khuyến mãi', savedPoint)}
            </div>
            <div class="view-bar">
                <div class="chip-row" id="mine-filter" role="group" aria-label="Lọc theo trạng thái"></div>
            </div>
            <div class="mine-list" id="mine-list"></div>`;

        document.getElementById('mine-filter').addEventListener('click', e => {
            const chip = e.target.closest('[data-status]');
            if (!chip) return;
            mineStatus = chip.dataset.status;
            drawMine();
        });
        drawMine();
    }

    /** Hàng chip trạng thái + danh sách theo trạng thái đang chọn. Mỗi chip kèm số lượt để biết trước khi
     *  bấm là bên trong có gì — nhất là "Chờ trao quà", thứ người học quay lại đây để kiểm tra. */
    function drawMine() {
        const filterList = [
            ['', 'Tất cả', 'ph-list-bullets'],
            ['Pending', 'Chờ trao quà', 'ph-hourglass-medium'],
            ['Delivered', 'Đã trao quà', 'ph-seal-check'],
            ['Cancelled', 'Đã hủy, hoàn điểm', 'ph-arrow-u-up-left'],
        ];
        const countOf = (status) => status ? mineList.filter(x => x.status === status).length : mineList.length;

        document.getElementById('mine-filter').innerHTML = filterList.map(([status, label, icon]) => `
            <button type="button" class="chip ${mineStatus === status ? 'on' : ''}" data-status="${status}"
                    aria-pressed="${mineStatus === status}">
                <i class="ph ${icon}" aria-hidden="true"></i> ${label} <span class="n">${countOf(status)}</span>
            </button>`).join('');

        const list = mineStatus ? mineList.filter(x => x.status === mineStatus) : mineList;
        document.getElementById('mine-list').innerHTML = list.length
            ? list.map(mineRowHtml).join('')
            : `<div class="card"><div class="card-body empty">
                   <div class="big"><i class="ph ph-receipt"></i></div>
                   <p>Không có lượt đổi nào ở trạng thái này.</p>
               </div></div>`;
    }

    /** Một lượt đổi: món quà — số điểm — dòng tiến trình "Đã đổi → Chờ trao / Đã trao / Đã hủy". */
    function mineRowHtml(x) {
        const state = { Pending: 'pending', Delivered: 'delivered', Cancelled: 'cancelled' }[x.status] || 'pending';
        const handledAt = x.handledAt ? esc(formatDate(x.handledAt, true)) : '';

        const secondStep = state === 'pending'
            ? `<li class="now"><b>${esc(x.statusLabel)}</b><span>Có thông báo khi quà được trao</span></li>`
            : `<li class="${state === 'delivered' ? 'done' : 'cancel'}"><b>${esc(x.statusLabel)}</b><span>${handledAt}</span></li>`;

        return `<article class="mine-row s-${state}">
            <div class="mine-product">
                <span class="mine-thumb">${x.productImageUrl
                    ? `<img src="${esc(x.productImageUrl)}" alt="" loading="lazy">`
                    : '<i class="ph-duotone ph-gift" aria-hidden="true"></i>'}</span>
                <div style="min-width:0">
                    <b class="mine-name" title="${esc(x.productName)}">${esc(x.productName)}</b>
                    <div class="sub">${x.quantity} × ${fmtNum(x.unitPointCost)} điểm${x.savedPoint > 0
                        ? ` · <span class="saved"><i class="ph-fill ph-tag" aria-hidden="true"></i>
                              ${esc(x.promotionName || 'Khuyến mãi')} −${fmtNum(x.savedPoint)}</span>` : ''}</div>
                    ${x.note ? `<div class="mine-note"><i class="ph ph-chat-text" aria-hidden="true"></i><span>${esc(x.note)}</span></div>` : ''}
                </div>
            </div>
            <div class="mine-cost ${state === 'cancelled' ? 'refunded' : ''}">
                <div class="amt">${point(x.totalPoint)}</div>
                <div class="sub">${state === 'cancelled' ? 'đã hoàn lại' : 'điểm đã dùng'}</div>
            </div>
            <ol class="track">
                <li class="done"><b>Đã đổi</b><span>${esc(formatDate(x.createdAt, true))}</span></li>
                ${secondStep}
            </ol>
        </article>`;
    }

    /** Một ô số liệu ở dãy đầu khu. Màu truyền vào là màu của biểu tượng; nền lấy chính màu đó pha rất
     *  nhạt, để bốn ô đứng cạnh nhau vẫn phân biệt được mà không ô nào át lấy danh sách bên dưới. */
    function statTile(icon, color, label, value) {
        return `<div class="stat-tile">
            <span class="ico" style="color:${color}; background:color-mix(in srgb, ${color} 12%, var(--surface))">
                <i class="ph-fill ${icon}"></i></span>
            <div style="min-width:0">
                <div class="lab">${esc(label)}</div>
                <div class="val">${Number(value ?? 0).toLocaleString(Lang.locale)}</div>
            </div>
        </div>`;
    }

    // ==================== TAB: QUẢN LÝ SẢN PHẨM ====================
    async function renderProductManage() {
        const box = document.getElementById('tab-product');
        box.innerHTML = skeletonHtml('list', { count: 6, avatar: false });

        let list;
        // includeInactive: màn hình quản lý phải thấy cả món đã ngừng bán, nếu không thì bỏ tick "Còn bày
        // bán" là món biến mất khỏi mọi màn hình và không còn đường nào bật lại.
        try { list = await API.get('/shop/product?includeInactive=true'); }
        catch (ex) { box.innerHTML = ''; toastErr(ex.message); return; }

        // Ô gợi ý "Nhóm sản phẩm" trong hộp thoại đọc từ productList — đổ bản đầy đủ này vào để nhóm của
        // một món đã ngừng bán vẫn gợi ý được, thay vì bị gõ lại lệch một chữ thành nhóm thứ hai.
        productList = list;

        setNavNum('product', list.length);

        const selling = list.filter(x => x.isActive).length;
        const soldOut = list.filter(x => x.stock <= 0).length;
        const redeemed = list.reduce((sum, x) => sum + x.redeemedCount, 0);

        box.innerHTML = `
            <div class="stat-strip">
                ${statTile('ph-package', 'var(--primary)', 'Tổng sản phẩm', list.length)}
                ${statTile('ph-storefront', 'var(--success)', 'Đang bày bán', selling)}
                ${statTile('ph-warning', 'var(--danger)', 'Hết hàng', soldOut)}
                ${statTile('ph-gift', 'var(--info)', 'Lượt đã đổi', redeemed)}
            </div>
            <div class="view-bar">
                <div class="grow search-box">
                    <i class="ph ph-magnifying-glass"></i>
                    <input id="m-keyword" maxlength="100" placeholder="Tìm theo tên hoặc nhóm sản phẩm">
                </div>
                <div class="chip-row" id="m-filter">
                    <button type="button" class="chip on" data-filter="">Tất cả</button>
                    <button type="button" class="chip" data-filter="selling">Đang bán</button>
                    <button type="button" class="chip" data-filter="soldout">Hết hàng</button>
                    <button type="button" class="chip" data-filter="off">Ngừng bán</button>
                </div>
                ${canCreate ? `<button class="btn" id="btn-add-product"><i class="ph ph-plus"></i> Thêm sản phẩm</button>` : ''}
            </div>
            <div id="manage-list"></div>`;

        document.getElementById('btn-add-product')?.addEventListener('click', () => openProductForm());
        document.getElementById('m-keyword').addEventListener('input', () => drawManageList(list));
        document.getElementById('m-filter').addEventListener('click', e => {
            const chip = e.target.closest('[data-filter]');
            if (!chip) return;
            document.querySelectorAll('#m-filter .chip').forEach(x => x.classList.toggle('on', x === chip));
            drawManageList(list);
        });
        drawManageList(list);
    }

    function drawManageList(list) {
        const keyword = normalize(document.getElementById('m-keyword').value);
        const mode = document.querySelector('#m-filter .chip.on')?.dataset.filter || '';

        // Ba bộ lọc là ba câu hỏi khác nhau của người quản kho: món nào đang bày, món nào cần nhập thêm,
        // món nào đã cất đi. "Hết hàng" vẫn tính cả món đã ngừng bán vì kho của nó vẫn là 0.
        const matchMode = (x) => mode === 'selling' ? x.isActive && x.stock > 0
            : mode === 'soldout' ? x.stock <= 0
            : mode === 'off' ? !x.isActive
            : true;

        const shown = list.filter(x => matchMode(x)
            && (!keyword || normalize(`${x.name} ${x.category || ''}`).includes(keyword)));
        const box = document.getElementById('manage-list');

        if (!shown.length) {
            box.innerHTML = `<div class="card"><div class="card-body empty">
                <div class="big"><i class="ph ph-package"></i></div>
                <p>${list.length ? 'Không có sản phẩm nào khớp điều kiện lọc.' : 'Cửa hàng chưa có sản phẩm nào.'}</p>
            </div></div>`;
            return;
        }

        box.innerHTML = `
            <div class="dl" style="--dl-cols: minmax(0,2.2fr) 118px 118px 92px 116px minmax(120px,auto)">
                <div class="dl-head">
                    <span>Sản phẩm</span><span>Giá điểm</span><span>Tồn kho</span>
                    <span>Đã đổi</span><span>Trạng thái</span><span style="text-align:right">Thao tác</span>
                </div>
                ${shown.map(x => `
                    <div class="dl-row ${x.isActive ? '' : 'off'}">
                        <div class="cell-product">
                            ${x.imageUrl ? `<img src="${esc(x.imageUrl)}" alt="">` : `<i class="ph ph-gift"></i>`}
                            <div style="min-width:0">
                                <b>${esc(x.name)}</b>
                                <div class="sub">${esc(x.category || 'Chưa phân nhóm')}</div>
                            </div>
                        </div>
                        <div class="nowrap">${point(x.pointCost)}</div>
                        <div><span class="badge ${x.stock <= 0 ? 'red' : x.stock <= 5 ? 'orange' : 'green'}">
                            ${x.stock <= 0 ? 'Hết hàng' : `Còn ${x.stock}`}</span></div>
                        <div>${x.redeemedCount}</div>
                        <div><span class="badge ${x.isActive ? 'blue' : 'gray'}">${x.isActive ? 'Đang bán' : 'Ngừng bán'}</span></div>
                        <div class="act">
                            <button class="btn ghost sm" data-who="${x.id}" title="Xem ai đã đổi sản phẩm này">
                                <i class="ph ph-users"></i> ${x.redeemedCount}</button>
                            ${canUpdate ? `<button class="btn ghost sm" data-edit="${x.id}" title="Sửa"><i class="ph ph-pencil-simple"></i></button>` : ''}
                            ${canDelete ? `<button class="btn ghost sm" data-del="${x.id}" title="Xóa"><i class="ph ph-trash"></i></button>` : ''}
                        </div>
                    </div>`).join('')}
            </div>`;

        const byId = new Map(list.map(x => [String(x.id), x]));
        box.querySelectorAll('[data-who]').forEach(b =>
            b.addEventListener('click', () => openRedeemerList(byId.get(b.dataset.who))));
        box.querySelectorAll('[data-edit]').forEach(b =>
            b.addEventListener('click', () => openProductForm(byId.get(b.dataset.edit))));
        box.querySelectorAll('[data-del]').forEach(b =>
            b.addEventListener('click', () => deleteProduct(byId.get(b.dataset.del))));
    }

    // ==================== HỘP THOẠI SẢN PHẨM ====================
    attachDropZone(document.getElementById('p-drop'), document.getElementById('p-file'), pickImage);
    document.getElementById('p-clear-image').addEventListener('click', () => setProductImage(''));

    function openProductForm(item) {
        document.getElementById('p-title').textContent = item ? 'Sửa sản phẩm' : 'Thêm sản phẩm';
        document.getElementById('p-id').value = item?.id || '';
        document.getElementById('p-name').value = item?.name || '';
        document.getElementById('p-category').value = item?.category || '';
        document.getElementById('p-cost').value = item?.pointCost ?? 100;
        document.getElementById('p-stock').value = item?.stock ?? 1;
        document.getElementById('p-description').value = item?.description || '';
        document.getElementById('p-active').checked = item ? item.isActive : true;
        setProductImage(item?.imageUrl || '');

        // Gợi ý các nhóm đã có để quản trị viên không gõ ra "Voucher" và "voucher" thành hai nhóm khác nhau.
        const categoryList = [...new Set(productList.map(x => x.category).filter(Boolean))].sort();
        document.getElementById('p-category-list').innerHTML =
            categoryList.map(c => `<option value="${esc(c)}">`).join('');

        openModal('modal-product');
    }

    function setProductImage(url) {
        document.getElementById('p-image-url').value = url || '';
        const preview = document.getElementById('p-preview');
        preview.src = url || '';
        preview.classList.toggle('hidden', !url);
        document.getElementById('p-clear-image').classList.toggle('hidden', !url);
        document.getElementById('p-drop-label').textContent = url
            ? 'Chọn ảnh khác để thay' : 'Kéo thả ảnh sản phẩm vào đây hoặc bấm để chọn';
    }

    /** Tải ảnh lên NGAY khi chọn, không đợi bấm Lưu: có ảnh thật trước mắt mới biết đã chọn đúng tệp chưa. */
    async function pickImage(file) {
        try {
            const result = await API.upload(file, 'shop');
            setProductImage(result.url);
        } catch (ex) { toastErr(ex.message); }
    }

    async function saveProduct(event) {
        event.preventDefault();
        const id = document.getElementById('p-id').value;
        const body = {
            name: document.getElementById('p-name').value.trim(),
            description: document.getElementById('p-description').value.trim() || null,
            category: document.getElementById('p-category').value.trim() || null,
            imageUrl: document.getElementById('p-image-url').value || null,
            pointCost: Number(document.getElementById('p-cost').value),
            stock: Number(document.getElementById('p-stock').value),
            isActive: document.getElementById('p-active').checked,
        };

        try {
            const result = id ? await API.put(`/shop/product/${id}`, body) : await API.post('/shop/product', body);
            closeModal('modal-product');
            toastOk(result.message);
            await openTab('product');
        } catch (ex) { toastErr(ex.message); }
    }

    async function deleteProduct(item) {
        if (!item) return;
        if (!await confirmDialog(`Xóa "${item.name}" khỏi cửa hàng?`)) return;
        try {
            toastOk((await API.del(`/shop/product/${item.id}`)).message);
            await openTab('product');
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== AI ĐÃ ĐỔI MÓN NÀY ====================
    async function openRedeemerList(item) {
        if (!item) return;
        document.getElementById('w-title').textContent = `Người đã đổi: ${item.name}`;
        document.getElementById('w-body').innerHTML = skeletonHtml('list', { count: 5 });
        openModal('modal-redeemer');

        let list;
        try { list = await API.get(`/shop/product/${item.id}/redemption`); }
        catch (ex) { document.getElementById('w-body').innerHTML = ''; toastErr(ex.message); return; }

        document.getElementById('w-body').innerHTML = list.length
            ? `<div class="table-wrap"><table>
                <thead><tr><th>Người đổi</th><th>SL</th><th>Điểm</th><th>Ngày đổi</th><th>Trạng thái</th></tr></thead>
                <tbody>${list.map(x => `<tr>
                    <td><div class="who">${avatarHtml(x)}<div>
                        <div class="name">${esc(x.userName)}</div>
                        <div class="sub">${esc([x.userEmployeeCode, x.userDepartmentName].filter(Boolean).join(' · ') || '—')}</div>
                    </div></div></td>
                    <td>${x.quantity}</td>
                    <td class="nowrap">${point(x.totalPoint)}</td>
                    <td class="nowrap">${esc(formatDate(x.createdAt, true))}</td>
                    <td><span class="badge ${STATUS_BADGE[x.status] || 'gray'}">${esc(x.statusLabel)}</span></td>
                </tr>`).join('')}</tbody></table></div>`
            : `<div class="empty"><div class="big"><i class="ph ph-users"></i></div>
                   <p>Chưa có ai đổi sản phẩm này.</p></div>`;
    }

    // ==================== TAB: KHUYẾN MÃI ====================
    async function renderPromotion() {
        const box = document.getElementById('tab-promo');
        box.innerHTML = skeletonHtml('list', { count: 4, avatar: false });

        let list;
        try {
            // includeAll: màn hình quản trị phải thấy cả đợt sắp tới, đã kết thúc và đang tắt — chỉ thấy đợt
            // đang chạy thì không có đường nào sửa lại một đợt vừa lỡ đặt sai ngày.
            [list, promoProductList] = await Promise.all([
                API.get('/shop/promotion?includeAll=true'),
                API.get('/shop/product?includeInactive=true'),
            ]);
        } catch (ex) { box.innerHTML = ''; toastErr(ex.message); return; }

        const running = list.filter(x => x.status === 'Running').length;
        const scheduled = list.filter(x => x.status === 'Scheduled').length;
        const savedTotal = list.reduce((sum, x) => sum + (x.savedPointTotal || 0), 0);
        setNavNum('promo', running, running > 0);

        box.innerHTML = `
            <div class="stat-strip">
                ${statTile('ph-confetti', 'var(--primary)', 'Tổng chương trình', list.length)}
                ${statTile('ph-play-circle', 'var(--success)', 'Đang chạy', running)}
                ${statTile('ph-calendar-dots', 'var(--info)', 'Sắp diễn ra', scheduled)}
                ${statTile('ph-piggy-bank', 'var(--warning)', 'Điểm đã giảm cho người học', savedTotal)}
            </div>
            <div class="view-bar">
                <div class="grow small muted">Trong thời gian đã đặt, sản phẩm thuộc phạm vi của đợt được đổi với
                    giá điểm rẻ hơn — giá niêm yết của sản phẩm không bị sửa.</div>
                ${canCreate ? `<button class="btn" id="btn-add-promo"><i class="ph ph-plus"></i> Tạo chương trình</button>` : ''}
            </div>
            ${running > 1 ? `<div class="card mb"><div class="card-body small" style="color:#b45309">
                <i class="ph ph-warning"></i> Đang có ${running} đợt chạy cùng lúc. Món nào bị nhiều đợt phủ sẽ
                lấy đợt cho giá rẻ nhất, không cộng dồn.</div></div>` : ''}
            <div id="promo-list"></div>`;

        document.getElementById('btn-add-promo')?.addEventListener('click', () => openPromotionForm());

        const listBox = document.getElementById('promo-list');
        if (!list.length) {
            listBox.innerHTML = `<div class="card"><div class="card-body empty">
                <div class="big"><i class="ph ph-confetti"></i></div>
                <p>Chưa có chương trình khuyến mãi nào.</p>
                ${canCreate ? '<p class="small muted">Tạo một đợt cho sự kiện sắp tới — đặt sẵn ngày bắt đầu, hệ thống tự áp giá đúng ngày.</p>' : ''}
            </div></div>`;
            return;
        }

        listBox.innerHTML = `
            <div class="dl" style="--dl-cols: minmax(0,2fr) 104px minmax(0,1fr) 158px 128px 112px minmax(88px,auto)">
                <div class="dl-head">
                    <span>Chương trình</span><span>Mức giảm</span><span>Phạm vi</span><span>Thời gian</span>
                    <span>Đã dùng</span><span>Trạng thái</span><span style="text-align:right">Thao tác</span>
                </div>
                ${list.map(x => `
                    <div class="dl-row ${x.status === 'Ended' ? 'off' : ''}">
                        <div class="who">
                            <span class="avatar" style="border-radius:9px;background:${esc(x.colorHex)};color:#fff;font-size:16px">
                                <i class="ph-fill ph-${esc(x.icon || 'confetti')}"></i></span>
                            <div style="min-width:0">
                                <div class="name">${esc(x.name)}</div>
                                ${x.description ? `<div class="sub">${esc(x.description)}</div>` : ''}
                            </div>
                        </div>
                        <div class="nowrap"><b style="color:${esc(x.colorHex)}">${esc(x.discountLabel)}</b></div>
                        <div class="small">${esc(x.scopeLabel)}<div class="sub">${x.productCount} sản phẩm</div></div>
                        <div class="small nowrap">${esc(formatDate(x.startAt, true))}
                            <div class="sub">đến ${esc(formatDate(x.endAt, true))}</div></div>
                        <div class="small">${x.redemptionCount > 0
                            ? `${x.redemptionCount} lượt<div class="sub">giảm ${x.savedPointTotal.toLocaleString(Lang.locale)} điểm</div>`
                            : '<span class="muted">—</span>'}</div>
                        <div><span class="badge ${PROMO_STATUS_BADGE[x.status] || 'gray'}">${esc(x.statusLabel)}</span></div>
                        <div class="act">
                            ${canUpdate ? `<button class="btn ghost sm" data-promo-edit="${x.id}" title="Sửa"><i class="ph ph-pencil-simple"></i></button>` : ''}
                            ${canDelete ? `<button class="btn ghost sm" data-promo-del="${x.id}" title="Xóa"><i class="ph ph-trash"></i></button>` : ''}
                        </div>
                    </div>`).join('')}
            </div>`;

        const byId = new Map(list.map(x => [String(x.id), x]));
        listBox.querySelectorAll('[data-promo-edit]').forEach(b =>
            b.addEventListener('click', () => openPromotionForm(byId.get(b.dataset.promoEdit))));
        listBox.querySelectorAll('[data-promo-del]').forEach(b =>
            b.addEventListener('click', () => deletePromotion(byId.get(b.dataset.promoDel))));
    }

    // ---------- BIỂU MẪU CHƯƠNG TRÌNH ----------
    /** Gắn một lần lúc dựng trang: các ô trong biểu mẫu liên quan nhau (đổi kiểu giảm thì đổi nhãn, đổi
     *  phạm vi thì hiện/ẩn ô nhóm và ô tick sản phẩm) và mọi thay đổi đều vẽ lại phần xem trước. */
    function bindPromotionForm() {
        document.getElementById('pr-swatch-row').innerHTML = PROMO_COLOR_LIST
            .map(c => `<button type="button" class="promo-swatch" data-color="${c}" style="background:${c}"
                               title="${c}" aria-label="Màu ${c}"></button>`).join('');

        document.getElementById('pr-swatch-row').addEventListener('click', e => {
            const button = e.target.closest('[data-color]');
            if (!button) return;
            document.getElementById('pr-color').value = button.dataset.color;
            syncPromotionForm();
        });

        document.getElementById('pr-discount-type').addEventListener('change', syncPromotionForm);
        document.getElementById('pr-discount-value').addEventListener('input', syncPromotionForm);
        document.getElementById('pr-name').addEventListener('input', syncPromotionForm);
        document.getElementById('pr-scope').addEventListener('change', syncPromotionForm);
        document.getElementById('pr-category').addEventListener('change', syncPromotionForm);

        document.getElementById('pr-pick-all').addEventListener('click', () => togglePickAll(true));
        document.getElementById('pr-pick-none').addEventListener('click', () => togglePickAll(false));
        document.getElementById('pr-product-list').addEventListener('change', syncPickCount);
    }

    function togglePickAll(checked) {
        document.querySelectorAll('#pr-product-list input').forEach(x => { x.checked = checked; });
        syncPickCount();
    }

    function syncPickCount() {
        const total = document.querySelectorAll('#pr-product-list input').length;
        const picked = document.querySelectorAll('#pr-product-list input:checked').length;
        document.getElementById('pr-pick-count').textContent = `Đã chọn ${picked}/${total} sản phẩm`;
    }

    /** Đồng bộ nhãn, phần hiện/ẩn và khối xem trước theo những gì đang nhập. */
    function syncPromotionForm() {
        const isPercent = document.getElementById('pr-discount-type').value === '0';
        const value = Number(document.getElementById('pr-discount-value').value) || 0;
        const scope = document.getElementById('pr-scope').value;
        const color = document.getElementById('pr-color').value;

        document.getElementById('pr-value-label').textContent = isPercent ? 'Mức giảm (%) *' : 'Số điểm trừ thẳng *';
        // Trần 100 chỉ đúng với kiểu phần trăm; kiểu trừ điểm thì mức giảm phụ thuộc giá từng món.
        document.getElementById('pr-discount-value').max = isPercent ? 100 : '';

        document.getElementById('pr-category-field').classList.toggle('hidden', scope !== '1');
        document.getElementById('pr-product-field').classList.toggle('hidden', scope !== '2');
        document.getElementById('pr-scope-hint').textContent = {
            '0': 'Mọi món đang bày bán, kể cả món thêm mới giữa đợt.',
            '1': 'Mọi món thuộc nhóm đã chọn, kể cả món thêm mới vào nhóm đó giữa đợt.',
            '2': 'Đúng những món được tick dưới đây — món thêm mới sau đó sẽ không được giảm.',
        }[scope] || '';

        document.querySelectorAll('.promo-swatch').forEach(x =>
            x.classList.toggle('active', x.dataset.color === color));

        const scopeText = scope === '0' ? 'Toàn bộ cửa hàng'
            : scope === '1' ? `Nhóm "${document.getElementById('pr-category').value || '—'}"`
            : 'Sản phẩm đã chọn';

        const preview = document.getElementById('pr-preview');
        preview.style.setProperty('--promo-color', color);
        preview.querySelector('.txt b').textContent = document.getElementById('pr-name').value.trim() || 'Tên chương trình';
        preview.querySelector('.txt div').textContent = scopeText;
        preview.querySelector('.cut').textContent = isPercent ? `-${value}%` : `-${value} điểm`;
    }

    function openPromotionForm(item) {
        document.getElementById('pr-title').textContent = item ? 'Sửa chương trình khuyến mãi' : 'Tạo chương trình khuyến mãi';
        document.getElementById('pr-id').value = item?.id || '';
        document.getElementById('pr-name').value = item?.name || '';
        document.getElementById('pr-description').value = item?.description || '';
        document.getElementById('pr-color').value = item?.colorHex || PROMO_COLOR_LIST[0];
        document.getElementById('pr-discount-type').value = item?.discountType === 'FixedPoint' ? '1' : '0';
        document.getElementById('pr-discount-value').value = item?.discountValue ?? 20;
        document.getElementById('pr-active').checked = item ? item.isActive : true;

        const scopeIndex = { AllProduct: '0', Category: '1', SelectedProduct: '2' };
        document.getElementById('pr-scope').value = item ? scopeIndex[item.scopeType] : '0';

        // Nhóm sản phẩm suy ra từ chính các món đang có — gõ tay thì chỉ cần sai một dấu cách là đợt
        // khuyến mãi không khớp món nào mà chẳng có gì báo.
        const categoryList = [...new Set(promoProductList.map(x => x.category).filter(Boolean))].sort();
        document.getElementById('pr-category').innerHTML = categoryList.length
            ? categoryList.map(c => `<option value="${esc(c)}">${esc(c)}</option>`).join('')
            : '<option value="">— Chưa có nhóm nào —</option>';
        if (item?.scopeCategory) document.getElementById('pr-category').value = item.scopeCategory;

        const pickedSet = new Set(item?.productIdList || []);
        document.getElementById('pr-product-list').innerHTML = promoProductList.length
            ? promoProductList.map(x => `
                <label><input type="checkbox" value="${x.id}" ${pickedSet.has(x.id) ? 'checked' : ''}>
                    <span>${esc(x.name)}${x.isActive ? '' : ' (ngừng bán)'}</span>
                    <span class="cost">${x.pointCost.toLocaleString(Lang.locale)} điểm</span></label>`).join('')
            : '<div class="small muted">Cửa hàng chưa có sản phẩm nào để chọn.</div>';
        syncPickCount();

        // Mặc định cho đợt mới: từ bây giờ tới hết 7 ngày nữa — một đợt sự kiện điển hình, và quan trọng hơn
        // là hai ô ngày không bao giờ để trống khiến trình duyệt chặn gửi mà không nói rõ ô nào sai.
        const start = item ? new Date(item.startAt) : new Date();
        const end = item ? new Date(item.endAt) : new Date(Date.now() + 7 * 86400000);
        document.getElementById('pr-start').value = toLocalInput(start);
        document.getElementById('pr-end').value = toLocalInput(end);

        syncPromotionForm();
        openModal('modal-promo');
    }

    /** Date -> chuỗi "yyyy-MM-ddTHH:mm" cho <input type="datetime-local">, theo giờ ĐỊA PHƯƠNG.
     *  toISOString() trả giờ UTC nên ở múi giờ Việt Nam sẽ lùi mất 7 tiếng ngay khi mở biểu mẫu. */
    function toLocalInput(date) {
        const pad = (n) => String(n).padStart(2, '0');
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
            + `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    }

    async function savePromotion(event) {
        event.preventDefault();
        const id = document.getElementById('pr-id').value;
        const scope = Number(document.getElementById('pr-scope').value);

        const body = {
            name: document.getElementById('pr-name').value.trim(),
            description: document.getElementById('pr-description').value.trim() || null,
            colorHex: document.getElementById('pr-color').value,
            icon: 'confetti',
            // Enum gửi lên bằng SỐ — dự án không bật JsonStringEnumConverter, xem Program.cs.
            discountType: Number(document.getElementById('pr-discount-type').value),
            discountValue: Number(document.getElementById('pr-discount-value').value),
            scopeType: scope,
            scopeCategory: scope === 1 ? document.getElementById('pr-category').value : null,
            productIdList: scope === 2
                ? [...document.querySelectorAll('#pr-product-list input:checked')].map(x => Number(x.value))
                : [],
            startAt: document.getElementById('pr-start').value,
            endAt: document.getElementById('pr-end').value,
            isActive: document.getElementById('pr-active').checked,
        };

        try {
            const result = id ? await API.put(`/shop/promotion/${id}`, body) : await API.post('/shop/promotion', body);
            closeModal('modal-promo');
            toastOk(result.message);
            await openTab('promo');
        } catch (ex) { toastErr(ex.message); }
    }

    async function deletePromotion(item) {
        if (!item) return;
        const extra = item.redemptionCount > 0
            ? ` Đã có ${item.redemptionCount} lượt đổi trong đợt này — các lượt đó giữ nguyên giá đã trả và vẫn đọc được tên chương trình.`
            : '';
        if (!await confirmDialog(`Xóa chương trình "${item.name}"?${extra}`)) return;
        try {
            toastOk((await API.del(`/shop/promotion/${item.id}`)).message);
            await openTab('promo');
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== TAB: ĐƠN ĐỔI QUÀ ====================
    async function renderOrder() {
        const box = document.getElementById('tab-order');
        // Chỉ dựng khung lọc một lần: dựng lại mỗi lượt tải sẽ xóa mất trạng thái lọc mà quản trị viên vừa
        // chọn, ngay sau mỗi lần bấm "Đã trao".
        if (!document.getElementById('o-status')) {
            box.innerHTML = `
                <div class="stat-strip" id="order-stat"></div>
                <div class="view-bar">
                    <div class="grow search-box">
                        <i class="ph ph-magnifying-glass"></i>
                        <input id="o-keyword" maxlength="100"
                               placeholder="Tìm theo tên người, mã nhân viên hoặc tên sản phẩm — VD: Nguyễn Văn A, NV001">
                    </div>
                    <button class="btn ghost" id="o-search"><i class="ph ph-magnifying-glass"></i> Tìm</button>
                </div>
                <div class="view-bar">
                    <div class="chip-row" id="o-status-row">
                        <button type="button" class="chip on" data-status="Pending"><i class="ph ph-hourglass"></i> Chờ trao quà</button>
                        <button type="button" class="chip" data-status="Delivered"><i class="ph ph-check-circle"></i> Đã trao quà</button>
                        <button type="button" class="chip" data-status="Cancelled"><i class="ph ph-arrow-u-up-left"></i> Đã hủy, hoàn điểm</button>
                        <button type="button" class="chip" data-status="">Tất cả</button>
                    </div>
                </div>
                <input type="hidden" id="o-status" value="Pending">
                <div id="order-list"></div>`;

            // Trạng thái chuyển từ ô xổ xuống sang hàng chip: đây là bộ lọc người quản trị bấm đi bấm lại
            // suốt buổi, và chip cho thấy luôn mình đang đứng ở nhóm nào mà không phải mở ô ra xem.
            // Vẫn giữ một ô ẩn #o-status làm nơi lưu giá trị, để phần tải dữ liệu bên dưới không đổi.
            document.getElementById('o-status-row').addEventListener('click', e => {
                const chip = e.target.closest('[data-status]');
                if (!chip) return;
                document.querySelectorAll('#o-status-row .chip').forEach(x => x.classList.toggle('on', x === chip));
                document.getElementById('o-status').value = chip.dataset.status;
                renderOrder();
            });
            document.getElementById('o-search').addEventListener('click', renderOrder);
            document.getElementById('o-keyword').addEventListener('keydown',
                e => { if (e.key === 'Enter') renderOrder(); });
        }

        const listBox = document.getElementById('order-list');
        listBox.innerHTML = skeletonHtml('list', { count: 5 });

        const query = new URLSearchParams();
        const status = document.getElementById('o-status').value;
        const keyword = document.getElementById('o-keyword').value.trim();
        if (status) query.set('status', status);
        if (keyword) query.set('keyword', keyword);

        let list;
        try { list = await API.get(`/shop/redemption?${query}`); }
        catch (ex) { listBox.innerHTML = ''; toastErr(ex.message); return; }

        // Số liệu tính trên ĐÚNG tập vừa lọc — nói rõ trong nhãn để không ai đọc nhầm thành số toàn hệ thống.
        const totalPoint = list.reduce((sum, x) => sum + x.totalPoint, 0);
        const totalItem = list.reduce((sum, x) => sum + x.quantity, 0);
        const peopleCount = new Set(list.map(x => x.userName)).size;
        document.getElementById('order-stat').innerHTML = `
            ${statTile('ph-clipboard-text', 'var(--primary)', 'Lượt đổi đang xem', list.length)}
            ${statTile('ph-gift', 'var(--info)', 'Tổng số quà', totalItem)}
            ${statTile('ph-users', 'var(--success)', 'Số người', peopleCount)}
            ${statTile('ph-diamond', 'var(--warning)', 'Tổng điểm', totalPoint)}`;

        if (!list.length) {
            listBox.innerHTML = `<div class="card"><div class="card-body empty">
                <div class="big"><i class="ph ph-clipboard-text"></i></div>
                <p>Không có lượt đổi nào khớp điều kiện lọc.</p>
            </div></div>`;
            return;
        }

        listBox.innerHTML = `
            <div class="dl" style="--dl-cols: minmax(0,1.4fr) minmax(0,1.4fr) 52px 110px minmax(0,1fr) minmax(150px,auto)">
                <div class="dl-head">
                    <span>Người đổi</span><span>Sản phẩm</span><span>SL</span><span>Điểm</span>
                    <span>Trạng thái</span><span style="text-align:right">Thao tác</span>
                </div>
                ${list.map(x => `
                    <div class="dl-row">
                        <div class="who">${avatarHtml(x)}<div style="min-width:0">
                            <div class="name">${esc(x.userName)}</div>
                            <div class="sub">${esc([x.userEmployeeCode, x.userDepartmentName].filter(Boolean).join(' · ') || '—')}</div>
                        </div></div>
                        <div class="cell-product">
                            ${x.productImageUrl ? `<img src="${esc(x.productImageUrl)}" alt="">` : `<i class="ph ph-gift"></i>`}
                            <div style="min-width:0">
                                <b>${esc(x.productName)}</b>
                                <div class="sub">${esc(formatDate(x.createdAt, true))}</div>
                            </div>
                        </div>
                        <div>${x.quantity}</div>
                        <div class="nowrap">${point(x.totalPoint)}</div>
                        <div>
                            <span class="badge ${STATUS_BADGE[x.status] || 'gray'}">${esc(x.statusLabel)}</span>
                            ${x.handledByName ? `<div class="sub">${esc(x.handledByName)} · ${esc(formatDate(x.handledAt))}</div>` : ''}
                            ${x.note ? `<div class="sub">${esc(x.note)}</div>` : ''}
                        </div>
                        <div class="act">
                            ${x.status === 'Pending' && canUpdate ? `
                                <button class="btn success sm" data-deliver="${x.id}"><i class="ph ph-check"></i> Đã trao</button>
                                <button class="btn ghost sm" data-cancel="${x.id}" title="Hủy và hoàn điểm cho người đổi">
                                    <i class="ph ph-arrow-u-up-left"></i></button>`
                                : '<span class="small muted">—</span>'}
                        </div>
                    </div>`).join('')}
            </div>`;

        const byId = new Map(list.map(x => [String(x.id), x]));
        listBox.querySelectorAll('[data-deliver]').forEach(b =>
            b.addEventListener('click', () => openHandle(byId.get(b.dataset.deliver), 'Delivered')));
        listBox.querySelectorAll('[data-cancel]').forEach(b =>
            b.addEventListener('click', () => openHandle(byId.get(b.dataset.cancel), 'Cancelled')));
    }

    function openHandle(item, status) {
        if (!item) return;
        const isCancel = status === 'Cancelled';

        document.getElementById('h-id').value = item.id;
        // Gửi lên bằng SỐ chứ không phải tên: dự án không bật JsonStringEnumConverter (xem
        // Program.cs → AddControllers), nên chuỗi "Delivered" trong thân JSON không dịch ngược
        // thành enum được. Giá trị phải khớp ShopRedemptionStatus trong Helpers/AppEnum.cs.
        document.getElementById('h-status').value = isCancel ? 2 : 1;
        document.getElementById('h-title').textContent = isCancel ? 'Hủy lượt đổi và hoàn điểm' : 'Xác nhận đã trao quà';
        document.getElementById('h-note').value = '';
        document.getElementById('h-note-intro').innerHTML = isCancel
            ? `Hủy lượt đổi <b>${esc(item.quantity)} × ${esc(item.productName)}</b> của <b>${esc(item.userName)}</b>:
               hoàn lại <b>${item.totalPoint.toLocaleString(Lang.locale)} điểm</b> và trả ${item.quantity} sản phẩm về kho.`
            : `Xác nhận đã trao <b>${esc(item.quantity)} × ${esc(item.productName)}</b> cho
               <b>${esc(item.userName)}</b>. Sau bước này lượt đổi không sửa lại được nữa.`;

        const submit = document.getElementById('h-submit');
        submit.className = isCancel ? 'btn danger' : 'btn success';
        submit.textContent = isCancel ? 'Hủy và hoàn điểm' : 'Xác nhận đã trao';

        openModal('modal-handle');
    }

    async function submitHandle(event) {
        event.preventDefault();
        const id = document.getElementById('h-id').value;
        const body = {
            status: Number(document.getElementById('h-status').value),
            note: document.getElementById('h-note').value.trim() || null,
        };

        try {
            const result = await API.put(`/shop/redemption/${id}/status`, body);
            closeModal('modal-handle');
            toastOk(result.message);
            await renderOrder();
            // Vừa bớt đi một việc: con số cảnh báo trên tab phải giảm theo, không thì nó vẫn báo có việc
            // cho tới lần tải lại trang.
            refreshPendingBadge();
        } catch (ex) { toastErr(ex.message); }
    }
})();
