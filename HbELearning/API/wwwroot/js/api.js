// File: wwwroot/js/api.js
// Lớp giao tiếp với Web API + quản lý phiên đăng nhập.

const TOKEN_KEY = 'hbe_token';
const PROFILE_KEY = 'hbe_profile';

const Auth = {
    get token() { return localStorage.getItem(TOKEN_KEY); },

    get profile() {
        try { return JSON.parse(localStorage.getItem(PROFILE_KEY) || 'null'); }
        catch { return null; }
    },

    /** Tên vai trò THẬT (VD "Trưởng phòng IT") — dùng để hiển thị. Muốn kiểm tra quyền thì dùng hasRole(). */
    get role() { return this.profile?.roleName ?? ''; },

    /**
     * Mọi tên vai trò mà người này được coi là đang giữ: vai trò của chính họ, cộng vai trò CHA nếu vai trò
     * đó kế thừa. Khớp đúng với hai claim vai trò mà server đặt vào token (xem Services/TokenService.cs) —
     * hai bên phải hiểu giống nhau, nếu không giao diện giấu mất menu của một trang mà API vẫn cho vào.
     */
    get roleList() {
        return [this.role, this.profile?.parentRoleName].filter(Boolean);
    },

    /** Người này có được coi là giữ vai trò `name` không (kể cả qua kế thừa). */
    hasRole(name) { return this.roleList.includes(name); },

    get isLoggedIn() { return !!this.token; },
    get isAdmin() { return this.hasRole('Admin'); },
    get isTrainer() { return this.hasRole('Trưởng Nhóm'); },
    get isInstructor() { return this.hasRole('Giảng Viên'); },
    get isManagingDirector() { return this.hasRole('Tổng Giám Đốc'); },
    get isManager() { return this.isAdmin || this.isTrainer || this.isInstructor; },
    get isApprovalDelegate() { return !!this.profile?.isApprovalDelegate; },

    /** Vai trò của người này đang được giao ít nhất một bước trong một luồng cấp duyệt còn hoạt động — họ cần
     * vào được màn hình khóa học để duyệt dù vai trò đó không thuộc nhóm quản lý khóa học thông thường. */
    get isWorkflowApprover() { return !!this.profile?.isWorkflowApprover; },

    save(token, profile) {
        localStorage.setItem(TOKEN_KEY, token);
        localStorage.setItem(PROFILE_KEY, JSON.stringify(profile));
    },

    /** Ghi đè hồ sơ đang lưu mà giữ nguyên phiên đăng nhập — dùng khi làm mới hồ sơ từ /auth/me. */
    saveProfile(profile) { localStorage.setItem(PROFILE_KEY, JSON.stringify(profile)); },

    /**
     * Vé ngắn hạn để mở kết nối SignalR (hộp chat, phòng học online).
     *
     * KHÔNG dùng thẳng Auth.token cho kênh thời gian thực: WebSocket không đặt được header Authorization
     * nên thư viện SignalR phải nhét thẻ vào ĐỊA CHỈ (?access_token=...), mà địa chỉ thì bị ghi lại trong
     * nhật ký của máy chủ, của proxy và trong lịch sử trình duyệt. Thẻ đăng nhập sống 8 tiếng, còn vé này
     * sống 2 phút và máy chủ từ chối nó ở mọi đường ngoài /hubs.
     *
     * Xin vé hỏng thì lùi về dùng thẻ đăng nhập: mất chat vì một lượt gọi lỗi còn tệ hơn.
     */
    async hubTicket() {
        try {
            // getSilent: vé được xin lại ở mỗi lần nối lại kênh, không nên để màn hình chớp loading mỗi lần.
            const result = await API.getSilent('/auth/hub-ticket');
            return result.ticket;
        } catch (ex) {
            console.warn('Khong xin duoc ve SignalR, tam dung the dang nhap:', ex.message);
            return this.token;
        }
    },

    clear() {
        localStorage.removeItem(TOKEN_KEY);
        localStorage.removeItem(PROFILE_KEY);
    },

    logout() {
        this.clear();
        location.href = '/login.html';
    },

    /**
     * Quyền thực tế của người đang đăng nhập: quyền của VAI TRÒ giao với quyền cấp riêng cho cá nhân.
     * Trả null khi vai trò chưa được cấu hình quyền — khi đó hệ thống dùng lại cách chặn theo tên vai trò như cũ.
     * Phải khớp Helpers/FunctionKey.cs Effective() ở server.
     */
    get functionKeyList() {
        const split = (raw) => String(raw || '').split(',').map(x => x.trim()).filter(Boolean)
            // Dữ liệu cũ lưu khóa trần ("course") được hiểu là quyền xem chức năng đó.
            .map(x => x.includes('.') ? x : x + '.view');

        const roleKeyList = split(this.profile?.roleFunctionKeys);
        if (!roleKeyList.length) return null;

        const userKeyList = split(this.profile?.functionKeys);
        return userKeyList.length ? roleKeyList.filter(x => userKeyList.includes(x)) : roleKeyList;
    },

    /** Có quyền "chức năng.thao tác" này không. Trả null khi vai trò chưa cấu hình quyền (chưa xác định). */
    can(functionKey) {
        const keyList = this.functionKeyList;
        return keyList === null ? null : keyList.includes(functionKey);
    },

    /**
     * Chặn truy cập trang nếu chưa đăng nhập / không đủ quyền.
     * @param {string[]} roleList vai trò được vào (cách cũ, dùng khi vai trò chưa cấu hình quyền)
     * @param {string} functionKey khóa quyền của trang, VD "user.view" — có quyền này thì vào được
     *                 dù tên vai trò không nằm trong roleList (phục vụ vai trò do Admin tự tạo).
     */
    require(roleList, functionKey) {
        if (!this.isLoggedIn) { location.href = '/login.html'; return false; }

        const allowed = functionKey ? this.can(functionKey) : null;
        const passed = allowed === null
            ? (!roleList || roleList.some(x => this.hasRole(x)))
            : allowed;

        if (!passed) {
            alert('Bạn không có quyền truy cập chức năng này.');
            location.href = '/pages/dashboard.html';
            return false;
        }
        return true;
    }
};

/**
 * Câu thông báo cho những lỗi KHÔNG kèm nội dung trả về.
 *
 * Mọi Controller trong dự án đều trả MessageResponse kèm câu tiếng Việt, nên hàm này chỉ chạy khi chính
 * hạ tầng từ chối request trước khi tới được Controller — lúc đó thân phản hồi rỗng và người dùng nhận được
 * một dòng "Lỗi 404" không nói lên điều gì, cũng không gợi ý được phải làm gì tiếp.
 *
 * Riêng 404 rỗng gần như luôn có một nguyên nhân: đường dẫn không khớp route nào — thường là mã nguồn đã
 * có chức năng mới nhưng tiến trình đang chạy vẫn là bản cũ. Nói thẳng ra để khỏi phải đi dò nh᪭t ký máy chủ.
 */
function fallbackErrorMessage(status) {
    return {
        400: 'Dữ liệu gửi lên không hợp lệ.',
        403: 'Bạn không có quyền thực hiện thao tác này.',
        404: 'Máy chủ không có chức năng này. Nếu vừa cập nhật mã nguồn, hãy khởi động lại ứng dụng rồi thử lại.',
        405: 'Thao tác này không được chấp nhận trên đường dẫn vừa gọi.',
        408: 'Máy chủ xử lý quá lâu. Hãy thử lại.',
        409: 'Dữ liệu đang bị trùng hoặc vừa được người khác thay đổi.',
        413: 'Tệp gửi lên quá lớn.',
        500: 'Máy chủ gặp lỗi khi xử lý yêu cầu này.',
        502: 'Không liên lạc được với máy chủ.',
        503: 'Máy chủ đang bận hoặc tạm ngưng. Hãy thử lại sau ít phút.'
    }[status] || `Máy chủ trả về lỗi ${status}.`;
}

/** Lỗi trả về từ API, kèm mã HTTP. */
class ApiError extends Error {
    /**
     * @param {any} data Thân phản hồi đã giải mã JSON, nguyên vẹn như máy chủ gửi.
     *
     * Giữ lại vì một vài lỗi mang theo dữ liệu chứ không chỉ một câu chữ: trang đăng nhập cần
     * remainingAttempt / isLockedOut của LoginFailedResponse để đếm ngược số lần còn lại và khóa biểu mẫu.
     * Không có nó thì chỗ gọi phải đi dò con số trong câu thông báo tiếng Việt — hỏng ngay khi đổi câu chữ.
     */
    constructor(message, status, data = null) {
        super(message);
        this.status = status;
        this.data = data;
    }
}

// ==================== LỚP LOADING TOÀN HỆ THỐNG ====================
// Tự động che toàn màn hình kèm vòng xoay + chữ "Đang xử lý..." mỗi khi có request GHI đang chạy (bất kể
// bấm nút/link nào gọi tới API) — không cần gắn loading riêng cho từng nút bấm. Ngoài việc báo hiệu, lớp
// che này còn chặn luôn cú bấm thứ hai vào nút Lưu/Xóa khi cú thứ nhất chưa xong.
//
// Request ĐỌC (GET) thì KHÔNG che: các màn hình danh sách đã tự vẽ khung xương (skeleton) ngay tại chỗ dữ
// liệu sắp hiện ra — vừa cho thấy đang tải, vừa cho thấy trước hình hài nội dung sắp tới. Che thêm cả màn
// hình lên trên thì khung xương vẽ ra cũng không ai nhìn thấy, mà thao tác đọc vốn cũng chẳng cần chặn.
//
// Ngoại lệ: request gọi qua API.getSilent / API.postSilent sẽ bỏ qua overlay — dùng cho polling nền.
(function ensureGlobalLoadingOverlay() {
    if (document.getElementById('global-loading-overlay')) return;
    const overlay = document.createElement('div');
    overlay.id = 'global-loading-overlay';
    overlay.innerHTML = `
        <div class="global-loading-box">
            <div class="spinner-lg"></div>
            <div class="global-loading-text">Đang xử lý...</div>
        </div>`;
    document.body.appendChild(overlay);
})();

let _activeRequestCount = 0;
// Đếm riêng số request ĐANG che màn hình. Không gộp chung với _activeRequestCount được, vì bây giờ hai
// việc đã tách nhau: mọi request đều ghim chiều cao trang, nhưng chỉ request ghi mới bật overlay.
let _overlayRequestCount = 0;
let _loadingShownAt = 0;
const GLOBAL_LOADING_MIN_VISIBLE_MS = 300;

// ==================== GIỮ CHỖ ĐANG ĐỨNG KHI TẢI LẠI DANH SÁCH ====================
// Mọi màn hình danh sách khi tải lại đều xóa trắng vùng nội dung trước rồi mới đổ dữ liệu mới vào
// (box.innerHTML = loadingHtml()), sau đó mới gọi API. Trong khoảnh khắc đó cả trang chỉ còn cao vài
// chục pixel, trình duyệt thấy thanh cuộn đang ở chỗ không còn tồn tại nên tự kéo về vị trí xa nhất
// còn hợp lệ — tức là về đầu trang. Dữ liệu vẽ xong thì đã muộn, chỗ đang đứng mất rồi: sửa một dòng
// ở cuối bảng dài là mỗi lần bấm Lưu lại phải cuộn xuống tìm lại từ đầu.
//
// Cách chữa: lúc request đầu tiên bắt đầu, ghim chiều cao <body> đủ để vị trí cuộn hiện tại vẫn còn
// hợp lệ. Trang không bao giờ ngắn hơn chỗ đang đứng thì trình duyệt không có lý do gì kéo về đầu,
// nên không hề có cú nhảy nào để phải sửa lại sau — khác hẳn với việc cuộn trở lại sau khi vẽ xong,
// cách đó vẫn để lọt một nháy hình ở đầu trang.
//
// Ghim ở <body> vì thanh cuộn của các màn hình này là thanh cuộn của cả trang: sidebar dùng
// position: fixed, còn .content không có vùng cuộn riêng. Dùng min-height chứ không phải height nên
// nội dung mới dài hơn vẫn đẩy trang dài ra bình thường.

// Vị trí cuộn ghi lại từ chính sự kiện scroll, KHÔNG đọc window.scrollY lúc cần.
// Đọc window.scrollY sẽ ép trình duyệt tính lại bố cục ngay tại đó, mà lúc ấy vùng nội dung vừa bị xóa
// trắng xong — giá trị trả về đã là 0 sau khi bị kéo về đầu, đúng cái ta đang muốn tránh.
let _scrollY = 0;
window.addEventListener('scroll', () => { _scrollY = window.scrollY; }, { passive: true });

function pinPageHeight() {
    // Đã ghim từ request trước thì giữ nguyên mốc cũ, đừng hạ xuống theo trang đang rỗng.
    if (document.body.style.minHeight) return;
    document.body.style.minHeight = `${_scrollY + window.innerHeight}px`;
}

function unpinPageHeight() {
    // Nhả ở khung hình kế tiếp: lúc đó đoạn code gọi API đã vẽ xong dữ liệu mới. Nếu danh sách mới
    // thật sự ngắn hơn (vừa xóa vài dòng) thì trang co lại ngay lúc này, đúng như phải thế.
    requestAnimationFrame(() => {
        // Còn request khác đang chạy thì chưa nhả — nhả giữa chừng là trang lại ngắn lại một nhịp.
        if (_activeRequestCount === 0) document.body.style.minHeight = '';
    });
}

/** Hiện ngay lập tức khi có request ghi, nhưng luôn giữ hiển thị tối thiểu 300ms dù request xong nhanh hơn —
 * để người dùng luôn thấy được trạng thái đang xử lý thay vì chớp nháy quá nhanh không kịp nhận ra.
 *
 * showOverlay = false (request đọc) vẫn ghim chiều cao trang như cũ: việc giữ chỗ đang đứng không liên quan
 * gì tới overlay, và khung xương của một danh sách dài vẫn có thể ngắn hơn danh sách thật. */
function beginGlobalLoading(showOverlay) {
    _activeRequestCount++;
    if (showOverlay) {
        _overlayRequestCount++;
        if (_overlayRequestCount === 1) {
            _loadingShownAt = Date.now();
            document.getElementById('global-loading-overlay')?.classList.add('active');
        }
    }
    pinPageHeight();
}

function endGlobalLoading(showOverlay) {
    _activeRequestCount = Math.max(0, _activeRequestCount - 1);
    if (_activeRequestCount === 0) unpinPageHeight();

    if (!showOverlay) return;
    _overlayRequestCount = Math.max(0, _overlayRequestCount - 1);
    if (_overlayRequestCount === 0) {
        const wait = Math.max(0, GLOBAL_LOADING_MIN_VISIBLE_MS - (Date.now() - _loadingShownAt));
        setTimeout(() => {
            if (_overlayRequestCount === 0) document.getElementById('global-loading-overlay')?.classList.remove('active');
        }, wait);
    }
}

/** signal (AbortSignal, tùy chọn): hủy request giữa chừng — VD người dùng đổi ảnh trong lúc AI còn đang đọc ảnh
 *  cũ. Request bị hủy ném nguyên AbortError, không bọc thành "không kết nối được máy chủ". */
async function request(method, path, body, isFormData = false, silent = false, signal = undefined) {
    const headers = {};
    if (Auth.token) headers['Authorization'] = `Bearer ${Auth.token}`;
    if (body && !isFormData) headers['Content-Type'] = 'application/json';

    // Chỉ request ghi mới che màn hình; request đọc để khung xương tại chỗ lo phần báo hiệu.
    const showOverlay = method !== 'GET';
    if (!silent) beginGlobalLoading(showOverlay);
    let response;
    try {
        try {
            response = await fetch(`/api${path}`, {
                method,
                headers,
                body: body ? (isFormData ? body : JSON.stringify(body)) : undefined,
                signal
            });
        } catch (ex) {
            if (ex?.name === 'AbortError') throw ex;
            throw new ApiError('Không kết nối được máy chủ. Kiểm tra ứng dụng đã chạy chưa.', 0);
        }

        // Hết hạn token -> quay về trang đăng nhập
        if (response.status === 401 && !location.pathname.endsWith('/login.html')) {
            Auth.clear();
            location.href = '/login.html';
            throw new ApiError('Phiên đăng nhập đã hết hạn.', 401);
        }

        if (response.status === 204) return null;

        const text = await response.text();
        let data = null;
        if (text) { try { data = JSON.parse(text); } catch { data = text; } }

        if (!response.ok) {
            let message = data?.message
                || data?.title
                || (typeof data === 'string' ? data : null)
                || fallbackErrorMessage(response.status);

            // Gom lỗi validation của ASP.NET Core
            if (data?.errors) {
                const detail = Object.values(data.errors).flat().join(' ');
                if (detail) message = detail;
            }
            throw new ApiError(message, response.status, data);
        }
        return data;
    } finally {
        if (!silent) endGlobalLoading(showOverlay);
    }
}

const API = {
    get: (path) => request('GET', path),
    post: (path, body) => request('POST', path, body),

    // Biến thể "im lặng": không bật overlay loading toàn màn hình — dành cho các request chạy nền
    // theo chu kỳ (đếm thông báo, heartbeat tiến độ học) để màn hình không tự chớp loading khi
    // người dùng không thao tác gì.
    getSilent: (path) => request('GET', path, undefined, false, true),
    postSilent: (path, body) => request('POST', path, body, false, true),
    // Cũng dùng khi ghi HÀNG LOẠT trong một vòng lặp (nhập CSV bảng dịch): mỗi dòng bật overlay một lần
    // thì màn hình chỉ toàn nhấp nháy, mà tiến độ đã có thông báo tổng kết ở cuối rồi.
    putSilent: (path, body) => request('PUT', path, body, false, true),
    put: (path, body) => request('PUT', path, body),
    patch: (path, body) => request('PATCH', path, body),
    del: (path) => request('DELETE', path),

    upload: (file, subFolder = 'lesson') => {
        const form = new FormData();
        form.append('file', file);
        return request('POST', `/upload?subFolder=${encodeURIComponent(subFolder)}`, form, true);
    },

    /** Gửi một FormData tự dựng (file + các trường khác) — dùng khi endpoint nhận cả tệp lẫn thông tin
     *  mô tả trong cùng một request, VD tải tệp lên Thư viện 3D. */
    postForm: (path, form) => request('POST', path, form, true),

    /** Như postForm nhưng KHÔNG che màn hình — dùng khi tự gửi NHIỀU tệp nối tiếp nhau và đã có chỗ báo tiến độ
     *  riêng (hộp chat gửi kèm cả một thư mục). Để nguyên lớp che thì một thư mục 30 tệp là 30 lần màn hình chớp tối,
     *  mà tệ hơn là nó chặn luôn mọi cú bấm suốt lượt gửi — kể cả bấm để dừng lại. */
    postFormSilent: (path, form, signal) => request('POST', path, form, true, true, signal),

    /** Gửi một file tới endpoint bất kỳ (dùng cho import Excel). */
    postFile: (path, file) => {
        const form = new FormData();
        form.append('file', file);
        return request('POST', path, form, true);
    },

    /**
     * Gửi file kèm callback báo % tiến độ tải lên — dùng XMLHttpRequest vì fetch()
     * không có sự kiện theo dõi tiến độ upload. Dùng cho import Excel để hiện thanh loading.
     */
    uploadWithProgress: (path, file, onProgress) => {
        const form = new FormData();
        form.append('file', file);
        return API.postFormWithProgress(path, form, onProgress);
    },

    /** Như uploadWithProgress nhưng gửi nguyên một FormData tự dựng (tệp + các trường mô tả) — dùng cho gói SCORM
     *  hàng trăm MB, nơi người soạn cần thấy % tải lên thay vì một màn hình chờ không biết bao lâu. */
    postFormWithProgress: (path, form, onProgress) => {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            xhr.open('POST', `/api${path}`);
            if (Auth.token) xhr.setRequestHeader('Authorization', `Bearer ${Auth.token}`);

            xhr.upload.onprogress = (e) => {
                if (e.lengthComputable && onProgress) onProgress(Math.round((e.loaded / e.total) * 100));
            };

            xhr.onload = () => {
                if (xhr.status === 401) {
                    Auth.clear();
                    location.href = '/login.html';
                    reject(new ApiError('Phiên đăng nhập đã hết hạn.', 401));
                    return;
                }
                let data = null;
                if (xhr.responseText) { try { data = JSON.parse(xhr.responseText); } catch { data = xhr.responseText; } }

                if (xhr.status >= 200 && xhr.status < 300) { resolve(data); return; }

                let message = data?.message || data?.title
                    || (typeof data === 'string' ? data : null) || `Lỗi ${xhr.status}`;
                if (data?.errors) {
                    const detail = Object.values(data.errors).flat().join(' ');
                    if (detail) message = detail;
                }
                reject(new ApiError(message, xhr.status));
            };

            xhr.onerror = () => reject(new ApiError('Không kết nối được máy chủ. Kiểm tra ứng dụng đã chạy chưa.', 0));
            xhr.send(form);
        });
    },

    /** Lấy một tệp từ endpoint cần đăng nhập về thành Blob — để hiện qua URL.createObjectURL trong <img> /
     *  <iframe>, vì hai thẻ đó không tự gửi được header Authorization (VD tệp quét chứng nhận bên ngoài). */
    blob: async (path) => {
        const headers = {};
        if (Auth.token) headers['Authorization'] = `Bearer ${Auth.token}`;
        const response = await fetch(`/api${path}`, { headers });
        if (!response.ok) {
            let message = 'Không tải được tệp.';
            try { message = (await response.json())?.message || message; } catch { /* thân không phải JSON */ }
            throw new ApiError(message, response.status);
        }
        return await response.blob();
    },

    /** Tải file từ endpoint có yêu cầu đăng nhập — thẻ <a> thông thường không gửi được token. */
    download: async (path, fileName) => {
        const headers = {};
        if (Auth.token) headers['Authorization'] = `Bearer ${Auth.token}`;

        beginGlobalLoading();
        try {
            const response = await fetch(`/api${path}`, { headers });
            if (!response.ok) throw new ApiError('Không tải được file mẫu.', response.status);

            const url = URL.createObjectURL(await response.blob());
            const link = document.createElement('a');
            link.href = url;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);
        } finally {
            endGlobalLoading();
        }
    }
};
