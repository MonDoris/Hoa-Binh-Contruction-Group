// File: wwwroot/js/chat.js
// ============================================================================================
// HỘP CHAT NỘI BỘ: nút nổi góc phải + bảng ba khung (danh sách hội thoại / khung tin nhắn / lập nhóm).
//
// Được app.js nạp động ở cuối mountLayout() (xem mountChatWidget), nên có mặt trên cả 25 trang mà
// không phải thêm thẻ <script> vào từng file HTML. Chat là thứ người ta dùng TRONG LÚC đang học hay
// đang duyệt khóa học — bắt chuyển sang một trang riêng để nhắn tin là hỏng mất công dụng.
//
// Gửi tin đi bằng REST, nhận tin về bằng SignalR — lý do xem chú thích đầu Hubs/ChatHub.cs.
//
// Gửi kèm được ảnh / tệp / cả một thư mục / nhãn dán / ghi âm. Mọi thứ đính kèm đều đi qua HAI bước:
// tải tệp lên trước (POST /chat/attachment) rồi mới gửi TIN mang đường dẫn vừa nhận. Gộp một bước
// (gửi thẳng multipart vào endpoint tin nhắn) thì mỗi tệp con của một thư mục lại phải mở một request
// mang đủ cả phần thân tin nhắn, mà lúc tải lên hỏng giữa chừng thì không phân biệt nổi "tệp hỏng"
// với "tin không gửi được".
// ============================================================================================

const ChatBox = (() => {
    let connection = null;
    let currentConversationId = null;
    let currentConversation = null;
    let conversationList = [];
    let onlineIdSet = new Set();

    // Người đang được tick ở khung lập nhóm / mời thêm thành viên. Map chứ không phải Set: bảng "đã chọn"
    // cần hiện TÊN, mà danh sách tìm kiếm thì đổi liên tục theo từ khóa — chỉ giữ id thì gõ từ khóa khác
    // một cái là những người đã tick trước đó biến mất khỏi màn hình dù vẫn đang được chọn.
    let pickedMemberMap = new Map();
    // Bộ lọc đang chọn ở cột trái: 'all' | 'unread' | 'group'.
    let listFilter = 'all';
    // Khung lập nhóm dùng lại cho cả việc MỜI THÊM người vào nhóm đã có. null = đang lập nhóm mới.
    let memberPickerTargetId = null;

    let mediaRecorder = null;
    let recordChunkList = [];
    let recordTimer = null;
    let recordStartAt = 0;
    let recordCancelled = false;

    const me = () => Auth.profile?.id;
    const el = (id) => document.getElementById(id);

    /** Bộ nhãn dán. Cố ý là EMOJI chứ không phải ảnh: một bộ sticker ảnh là vài trăm tệp phải tải về, phải
     *  đóng gói kèm bản cài, và phải xin bản quyền — trong khi emoji đã có sẵn trên mọi máy, phóng to lên
     *  là ra đúng cái cảm giác của một nhãn dán. */
    const STICKER_GROUP_LIST = [
        { name: 'Cảm xúc', list: ['😀', '😁', '😂', '🤣', '😊', '😍', '😘', '😎', '🤗', '🤔', '😴', '😭', '😡', '🥳', '🤯', '😇'] },
        { name: 'Cử chỉ', list: ['👍', '👎', '👌', '👏', '🙏', '💪', '🤝', '✌️', '🫡', '🙌', '👋', '🤞'] },
        { name: 'Công việc', list: ['✅', '❌', '⚠️', '📌', '📎', '📊', '📅', '⏰', '💡', '🔥', '🎯', '🚀'] },
        { name: 'Khác', list: ['❤️', '💯', '🎉', '🎂', '☕', '🍀', '⭐', '🌈', '🏆', '🎁', '📣', '🤖'] }
    ];

    /** Bộ nền khung chat dựng sẵn. Khóa phải khớp đúng ChatController.BackgroundKeySet ở server VÀ bộ
     *  [data-bg] trong css/style.css — lệch một chỗ là chọn xong không thấy gì đổi.
     *
     *  Cố ý KHÔNG cho tải ảnh nền riêng lên: nền trải kín khung chat, nên một tấm ảnh bất kỳ đặt vào đó
     *  thường nuốt mất chữ trong bong bóng. Mấy mẫu này pha từ một màu nhấn với chính màu nền của giao
     *  diện, nên chúng tự tối đi khi bật nền tối và chữ luôn còn đọc được. */
    const BACKGROUND_LIST = [
        { key: 'default', name: 'Mặc định' },
        { key: 'sky', name: 'Trời xanh' },
        { key: 'mint', name: 'Bạc hà' },
        { key: 'forest', name: 'Lá cây' },
        { key: 'sand', name: 'Cát' },
        { key: 'peach', name: 'Đào' },
        { key: 'rose', name: 'Hồng' },
        { key: 'lilac', name: 'Tím nhạt' },
        { key: 'slate', name: 'Xám đá' }
    ];

    function init() {
        // Không dựng được (trang không có thanh trên, VD trang đăng nhập) thì dừng hẳn — đừng mở kết nối
        // SignalR cho một hộp chat không có chỗ hiện ra.
        if (!render()) return;
        connect();
        refreshConversationList();
    }

    // ==================== KẾT NỐI THỜI GIAN THỰC ====================

    function connect() {
        connection = new signalR.HubConnectionBuilder()
            // Vé ngắn hạn thay cho thẻ đăng nhập — xem Auth.hubTicket(). SignalR gọi lại hàm này ở mỗi lần
            // kết nối và mỗi lần nối lại, nên vé luôn còn hạn.
            .withUrl('/hubs/chat', { accessTokenFactory: () => Auth.hubTicket() })
            .withAutomaticReconnect()          // mạng chập chờn / máy ngủ dậy thì tự nối lại
            .build();

        connection.on('ChatMessage', onIncoming);

        connection.on('MessageDeleted', (conversationId, messageId) => {
            if (conversationId !== currentConversationId) return;
            const row = document.querySelector(`[data-msg="${messageId}"]`);
            if (row) row.querySelector('.chat-body').innerHTML = '<div class="bubble"><i>Tin nhắn đã thu hồi</i></div>';
        });

        connection.on('PresenceChanged', (userId, isOnline) => {
            isOnline ? onlineIdSet.add(userId) : onlineIdSet.delete(userId);
            renderConversationList();
            // Dòng "Đang hoạt động" trên đầu khung tin nhắn cũng phải đổi theo, không chỉ chấm xanh ở cột trái.
            if (currentConversation?.partnerUserId === userId) renderThreadHeader();
        });

        // Lập nhóm, đổi tên nhóm, thêm/bớt thành viên — server báo về để danh sách bên trái khớp lại. Không
        // có tín hiệu này thì người được mời vào nhóm phải tự tải lại trang mới nhìn thấy nhóm của mình.
        connection.on('ConversationChanged', async (conversationId) => {
            await refreshConversationList();

            // Đang mở đúng hội thoại vừa đổi mà nó biến mất khỏi danh sách nghĩa là ta vừa bị mời ra (hoặc tự
            // rời) — đóng khung tin nhắn lại, đừng để mở một nhóm không còn quyền đọc.
            if (conversationId !== currentConversationId) return;
            const stillMine = conversationList.find(x => x.id === conversationId);
            if (!stillMine) { back(); return; }

            currentConversation = stillMine;
            renderThreadHeader();
        });

        connection.on('Typing', (conversationId) => {
            if (conversationId !== currentConversationId) return;
            const box = el('chat-typing');
            // Bong bóng "đang nhập" chiếm chỗ ngay dưới khung tin nhắn nên khung thấp đi một đoạn: đang đọc ở
            // cuối thì cuộn theo, kẻo tin mới nhất bị nó che mất một nửa.
            const list = el('chat-message-list');
            const atBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 40;
            box.classList.remove('hidden');
            if (atBottom) scrollToBottom();
            clearTimeout(box._timer);
            box._timer = setTimeout(() => box.classList.add('hidden'), 2500);
        });

        // Nối lại xong PHẢI tải lại lịch sử: quãng thời gian mất kết nối có tin nào tới thì ta không hề
        // nhận được — khung tin nhắn sẽ thủng một đoạn mà không ai biết.
        connection.onreconnected(() => {
            refreshConversationList();
            if (currentConversationId) openConversation(currentConversationId);
        });

        connection.start()
            .then(() => connection.invoke('GetOnlineUserIdList'))
            .then(idList => { onlineIdSet = new Set(idList); renderConversationList(); })
            .catch(() => { /* không có kênh thời gian thực thì vẫn đọc/gửi được bằng REST */ });
    }

    function onIncoming(dto) {
        const conversation = conversationList.find(x => x.id === dto.conversationId);
        if (conversation) {
            conversation.lastMessage = previewOf(dto);
            conversation.lastMessageAt = dto.createdAt;
            conversation.lastMessageIsMine = dto.senderUserId === me();
            if (dto.conversationId !== currentConversationId && dto.senderUserId !== me())
                conversation.unreadCount++;
            conversationList.sort((a, b) => new Date(b.lastMessageAt) - new Date(a.lastMessageAt));
            renderConversationList();
        } else {
            refreshConversationList();          // hội thoại mới toanh do người khác mở
        }

        if (dto.conversationId === currentConversationId) {
            appendMessage(dto);
            API.postSilent(`/chat/conversation/${dto.conversationId}/read`).catch(() => { });
            if (conversation) conversation.unreadCount = 0;
        } else if (dto.senderUserId !== me()) {
            toast(`${dto.senderName}: ${previewOf(dto).slice(0, 60)}`, 'info');
        }
        refreshBadge();
    }

    // ==================== DỮ LIỆU ====================

    async function refreshConversationList() {
        try { conversationList = await API.getSilent('/chat/conversation'); }
        catch { return; }
        renderConversationList();
        refreshBadge();
    }

    function refreshBadge() {
        const badge = el('chat-badge');
        const total = conversationList.reduce((sum, x) => sum + x.unreadCount, 0);
        badge.textContent = total > 99 ? '99+' : total;
        badge.classList.toggle('hidden', total === 0);
    }

    async function openConversation(id) {
        currentConversationId = id;
        currentConversation = conversationList.find(x => x.id === id) ?? null;
        closeMemberPicker();
        el('chat-widget').classList.add('thread-open');
        renderThreadHeader();
        closeMemberPanel();
        closeStickerPanel();
        closeBackgroundPanel();
        renderConversationList();        // tô sáng dòng vừa chọn ở cột trái
        el('chat-message-list').innerHTML = '';

        let list;
        try { list = await API.getSilent(`/chat/conversation/${id}/message`); }
        catch (e) { toast(e.message, 'error'); return; }

        el('chat-message-list').innerHTML = list.length
            ? list.map((m, i) => messageHtml(m, list[i - 1] ?? null, list[i + 1] ?? null)).join('')
            : `<div class="chat-hello">${conversationAvatarHtml(currentConversation, 'lg')}
                   <b>${esc(currentConversation?.title ?? '')}</b>
                   <small>Chưa có tin nhắn nào. Gửi lời chào để bắt đầu nhé 👋</small></div>`;
        scrollToBottom();
        // Khung tin nhắn vừa từ display:none chuyển sang hiện, nên bây giờ ô nhập mới đo được chiều cao
        // thật của nó — xem ghi chú trong syncComposer.
        syncComposer();
        // preventScroll: đặt con trỏ vào ô nhập mà KHÔNG để trình duyệt cuộn thứ gì để "khoe" nó ra.
        // Khung chat đã là overflow:clip nên không tự cuộn được nữa, nhưng nếu không chặn thì trình
        // duyệt sẽ tìm ngược lên vùng cuộn kế tiếp — chính là cả trang — và giật màn hình một cái.
        el('chat-input').focus({ preventScroll: true });

        await API.postSilent(`/chat/conversation/${id}/read`).catch(() => { });
        const conversation = conversationList.find(x => x.id === id);
        if (conversation) { conversation.unreadCount = 0; renderConversationList(); refreshBadge(); }
    }

    async function send() {
        const input = el('chat-input');
        const content = input.value.trim();
        if (!content || !currentConversationId) return;

        input.value = '';
        syncComposer();                            // ô nhập co lại một dòng, nút Gửi tắt đi
        try {
            // Không tự vẽ tin lên màn hình ở đây: server sẽ đẩy lại đúng tin đó qua hub cho MỌI thiết bị
            // của mình, vẽ trước là tin hiện hai lần trên tab đang mở.
            await API.postSilent(`/chat/conversation/${currentConversationId}/message`, { content });
        } catch (e) {
            input.value = content;                 // trả lại chữ đã gõ, đừng bắt gõ lại
            syncComposer();
            toast(e.message, 'error');
        }
    }

    /** Ô nhập cao dần theo số dòng đang gõ (tối đa ~5 dòng rồi tự cuộn), và nút Gửi tắt khi chưa có chữ nào.
     *
     *  Phải hạ height về 'auto' TRƯỚC khi đọc scrollHeight: giữ nguyên chiều cao cũ thì scrollHeight không
     *  bao giờ nhỏ hơn nó, nên ô nhập chỉ biết phình ra mà không co lại được lúc xóa bớt chữ. */
    function syncComposer() {
        const input = el('chat-input');
        input.style.height = 'auto';
        // scrollHeight bằng 0 khi khung hội thoại đang display:none (chưa mở cuộc nào) — đặt chiều cao
        // theo con số đó là ô nhập co về 0 pixel và biến mất hẳn ngay lúc mở ra.
        if (input.scrollHeight) input.style.height = `${Math.min(input.scrollHeight, 104)}px`;
        el('chat-send-btn').disabled = input.value.trim().length === 0;
    }

    async function startDirect(userId) {
        try {
            const conversation = await API.post(`/chat/direct/${userId}`);
            el('chat-search').value = '';
            el('chat-contact-list').classList.add('hidden');
            await refreshConversationList();
            openConversation(conversation.id);
        } catch (e) { toast(e.message, 'error'); }
    }

    // ==================== ĐÍNH KÈM: ẢNH / TỆP / THƯ MỤC / GHI ÂM ====================

    /** Tải một tệp lên rồi gửi tin mang đường dẫn vừa nhận. Trả về true nếu trót lọt.
     *
     *  displayName tách khỏi file.name để lượt gửi cả THƯ MỤC giữ được đường dẫn tương đối
     *  ("Hồ sơ/2026/CV.pdf") — bên nhận nhìn ra tệp nằm ở đâu trong cây thư mục gốc. */
    async function uploadAndSend(file, messageType, displayName = file.name, content = '') {
        const form = new FormData();
        form.append('file', file);

        let uploaded;
        try { uploaded = await API.postFormSilent('/chat/attachment', form); }
        catch (e) { toast(`${displayName}: ${e.message}`, 'error'); return false; }

        try {
            await API.postSilent(`/chat/conversation/${currentConversationId}/message`, {
                content,
                attachmentUrl: uploaded.url,
                messageType,
                attachmentName: displayName,
                attachmentSize: uploaded.sizeByte ?? file.size
            });
            return true;
        } catch (e) { toast(`${displayName}: ${e.message}`, 'error'); return false; }
    }

    /** Gửi lần lượt, KHÔNG chạy song song bằng Promise.all: chọn một thư mục 50 tệp mà bắn 50 request cùng
     *  lúc thì trình duyệt tự xếp hàng phần lớn trong số đó, còn server thì nhận một cú dội. Quan trọng hơn:
     *  gửi tuần tự mới giữ đúng THỨ TỰ tin nhắn, chứ chạy song song thì tệp nhẹ về đích trước và cả thư mục
     *  hiện ra lộn xộn. */
    async function sendFileList(fileList, messageType, nameOf = (f) => f.name) {
        if (!currentConversationId || fileList.length === 0) return;

        const bar = el('chat-upload');
        bar.classList.remove('hidden');
        let done = 0;

        for (const file of fileList) {
            bar.textContent = `Đang gửi ${done + 1}/${fileList.length}: ${nameOf(file)}`;
            // Ảnh trong một lượt chọn nhiều tệp vẫn là ảnh; tệp trong một thư mục thì luôn coi là tệp
            // (thư mục chứa đủ thứ, mà một cây ảnh 200 tấm bung hết ra trong khung chat thì không đọc nổi).
            const type = messageType === 'Image' && !isImage(file) ? 'File' : messageType;
            if (await uploadAndSend(file, type, nameOf(file))) done++;
        }

        bar.classList.add('hidden');
        bar.textContent = '';
        if (done > 1) toast(`Đã gửi ${done} tệp.`, 'success');
    }

    const isImage = (file) => (file.type || '').startsWith('image/');

    async function sendSticker(emoji) {
        closeStickerPanel();
        if (!currentConversationId) return;
        try {
            await API.postSilent(`/chat/conversation/${currentConversationId}/message`,
                { content: emoji, messageType: 'Sticker' });
        } catch (e) { toast(e.message, 'error'); }
    }

    // -------------------- Ghi âm --------------------

    async function toggleRecord() {
        if (mediaRecorder) { stopRecord(); return; }
        if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
            toast('Trình duyệt này không ghi âm được.', 'error');
            return;
        }

        let stream;
        try { stream = await navigator.mediaDevices.getUserMedia({ audio: true }); }
        catch { toast('Không truy cập được micro. Kiểm tra quyền của trình duyệt.', 'error'); return; }

        recordChunkList = [];
        recordCancelled = false;
        mediaRecorder = new MediaRecorder(stream);
        mediaRecorder.ondataavailable = e => { if (e.data.size > 0) recordChunkList.push(e.data); };

        mediaRecorder.onstop = async () => {
            // Nhả micro NGAY khi thu xong, đừng đợi gửi xong: còn giữ track là đèn micro trên máy vẫn sáng
            // và người dùng tưởng mình đang bị nghe lén.
            stream.getTracks().forEach(track => track.stop());
            clearInterval(recordTimer);
            const second = Math.round((Date.now() - recordStartAt) / 1000);
            mediaRecorder = null;
            el('chat-record-bar').classList.add('hidden');
            el('chat-mic-btn').classList.remove('recording');

            if (recordCancelled) return;              // bấm Hủy, hoặc đóng bảng chat giữa chừng
            // Bấm nhầm rồi nhả ngay — gửi đi thì người nhận nhận được một đoạn câm.
            if (second < 1) { toast('Đoạn ghi âm quá ngắn.', 'info'); return; }

            const blob = new Blob(recordChunkList, { type: 'audio/webm' });
            const file = new File([blob], `ghi-am-${Date.now()}.webm`, { type: 'audio/webm' });
            await uploadAndSend(file, 'Audio', `Tin nhắn thoại ${formatSecond(second)}`);
        };

        mediaRecorder.start();
        recordStartAt = Date.now();
        el('chat-mic-btn').classList.add('recording');
        el('chat-record-bar').classList.remove('hidden');
        const tick = () => el('chat-record-time').textContent = formatSecond(Math.round((Date.now() - recordStartAt) / 1000));
        tick();
        recordTimer = setInterval(tick, 250);
    }

    function stopRecord() { if (mediaRecorder?.state === 'recording') mediaRecorder.stop(); }

    /** Hủy: vẫn phải dừng máy thu (để onstop nhả micro và dọn giao diện) nhưng dựng cờ để nó bỏ qua lượt gửi. */
    function cancelRecord() {
        if (!mediaRecorder) return;
        recordCancelled = true;
        recordChunkList = [];
        stopRecord();
    }

    const formatSecond = (second) =>
        `${String(Math.floor(second / 60)).padStart(2, '0')}:${String(second % 60).padStart(2, '0')}`;

    function formatSize(byte) {
        if (!byte) return '';
        if (byte < 1024) return `${byte} B`;
        if (byte < 1024 * 1024) return `${(byte / 1024).toFixed(0)} KB`;
        return `${(byte / 1024 / 1024).toFixed(1)} MB`;
    }

    // ==================== NỀN KHUNG CHAT ====================

    /** Ảnh tự tải lên thắng mẫu dựng sẵn — server cũng chỉ cho một trong hai tồn tại. */
    function applyBackground(key, url) {
        const box = el('chat-message-list');

        if (url) {
            // Đưa đường dẫn vào một custom property rồi để style.css ghép nó với lớp phủ làm mờ. Gán thẳng
            // background-image ở đây thì mất lớp phủ đó, mà thiếu nó là chữ trong bong bóng nằm trên một
            // tấm ảnh đầy chi tiết và không còn đọc nổi.
            box.style.setProperty('--chat-bg-photo', 'url("' + url + '")');
            box.classList.add('has-photo');
            delete box.dataset.bg;
            return;
        }

        box.classList.remove('has-photo');
        box.style.removeProperty('--chat-bg-photo');
        // "Mặc định" thì GỠ hẳn thuộc tính chứ không đặt data-bg="default": để nguyên thì mọi hội thoại
        // đều khớp bộ chọn [data-bg] và phải viết thêm một luật chỉ để hủy luật trước đó.
        if (key && key !== 'default') box.dataset.bg = key;
        else delete box.dataset.bg;
    }

    function renderBackgroundPanel() {
        const photoUrl = currentConversation?.backgroundUrl;
        // Đang dùng ảnh riêng thì KHÔNG mẫu dựng sẵn nào được tô sáng, kể cả "Mặc định" — hai chỗ cùng
        // sáng sẽ nói dối rằng nền đang là mẫu đó.
        const current = photoUrl ? null : (currentConversation?.backgroundKey ?? 'default');

        el('chat-bg-panel').innerHTML = `
            <small>Nền khung chat — chỉ mình bạn nhìn thấy</small>
            <div class="chat-bg-grid">
                ${BACKGROUND_LIST.map(b => `
                    <button type="button" class="chat-bg-swatch${b.key === current ? ' active' : ''}"
                            data-bg="${b.key}" title="${esc(b.name)}"
                            onclick="ChatBox.setBackground('${b.key}')"></button>`).join('')}
                <button type="button" class="chat-bg-swatch photo${photoUrl ? ' active' : ''}"
                        title="Chọn ảnh trên máy làm nền" onclick="ChatBox.pickBackgroundImage()"
                        ${photoUrl ? `style="background-image:url('${esc(photoUrl)}')"` : ''}>
                    ${photoUrl ? '' : '<i class="ph ph-image-square"></i>'}
                </button>
            </div>`;
    }

    function toggleBackgroundPanel() {
        const panel = el('chat-bg-panel');
        const willOpen = panel.classList.contains('hidden');
        closeStickerPanel();                      // hai bảng cùng chỗ, mở cái này là đóng cái kia
        if (willOpen) renderBackgroundPanel();
        panel.classList.toggle('hidden', !willOpen);
    }

    function closeBackgroundPanel() { el('chat-bg-panel').classList.add('hidden'); }

    async function setBackground(key) {
        if (!currentConversationId) return;

        // Đổi ngay trên màn hình rồi mới gọi API: nền là thứ người ta bấm để XEM thử, chờ một vòng mạng
        // mới thấy kết quả thì không ai bấm lần thứ hai.
        applyBackground(key, null);
        if (currentConversation) {
            currentConversation.backgroundKey = key === 'default' ? null : key;
            currentConversation.backgroundUrl = null;   // server cũng xóa ảnh cũ đi trong cùng lượt gọi
        }
        renderBackgroundPanel();

        try {
            await API.postSilent(`/chat/conversation/${currentConversationId}/background`, { backgroundKey: key });
        } catch (e) { toast(e.message, 'error'); }
    }

    function pickBackgroundImage() { el('chat-file-background').click(); }

    async function uploadBackgroundImage(file) {
        if (!currentConversationId) return;

        const form = new FormData();
        form.append('file', file);
        try {
            const result = await API.postForm(`/chat/conversation/${currentConversationId}/background-image`, form);
            applyBackground(null, result.url);
            if (currentConversation) {
                currentConversation.backgroundUrl = result.url;
                currentConversation.backgroundKey = null;
            }
            renderBackgroundPanel();
            toast('Đã đổi ảnh nền khung chat.', 'success');
        } catch (e) { toast(e.message, 'error'); }
    }

    // ==================== NHÓM CHAT ====================

    /** Mở khung chọn người: không truyền gì = lập nhóm mới, truyền id = mời thêm vào nhóm đang mở. */
    function openMemberPicker(conversationId = null) {
        memberPickerTargetId = conversationId;
        pickedMemberMap = new Map();

        el('chat-group-title-box').classList.toggle('hidden', conversationId !== null);
        el('chat-picker-heading').textContent = conversationId === null ? 'Nhóm mới' : 'Thêm thành viên';
        el('chat-picker-submit').textContent = conversationId === null ? 'Lập nhóm' : 'Thêm vào nhóm';
        el('chat-group-title').value = '';
        el('chat-member-search').value = '';
        el('chat-member-result').innerHTML = '<div class="chat-empty">Gõ tên đồng nghiệp để tìm.</div>';
        renderPickedMemberList();

        el('chat-picker').classList.remove('hidden');
        el('chat-group-title').focus({ preventScroll: true });
    }

    /** Chỉ việc giấu lớp phủ đi. Trước đây đây là khung thứ BA trượt ngang, nên đóng nó lại còn phải nhớ
     *  xem lúc mở ra người dùng đang đứng ở danh sách hay đang đứng trong một hội thoại mà trả họ về đúng
     *  chỗ. Lớp phủ không có vấn đề đó: phía sau nó chưa bao giờ rời đi. */
    function closeMemberPicker() {
        el('chat-picker').classList.add('hidden');
        memberPickerTargetId = null;
    }

    /** Tên người đi qua data-name chứ KHÔNG nhúng thẳng vào onclick. Nhúng vào thì esc() đổi dấu nháy đơn
     *  thành &#39;, trình duyệt lại giải mã ngược nó về dấu nháy ngay lúc đọc thuộc tính, và một cái tên như
     *  O'Brien cắt đôi chuỗi trong onclick — cả nút bấm chết lặng với một lỗi cú pháp. */
    function pickFromNode(node) {
        togglePickMember(Number(node.dataset.uid), node.dataset.name ?? '');
    }

    function togglePickMember(userId, fullName) {
        pickedMemberMap.has(userId) ? pickedMemberMap.delete(userId) : pickedMemberMap.set(userId, fullName);
        renderPickedMemberList();
        syncPickedTick(userId);
    }

    /** Tô lại dấu tick trên dòng tương ứng ở danh sách kết quả tìm kiếm, nếu nó đang hiện. */
    function syncPickedTick(userId) {
        document.querySelectorAll(`#chat-member-result [data-uid="${userId}"]`)
            .forEach(node => node.classList.toggle('picked', pickedMemberMap.has(userId)));
    }

    function renderPickedMemberList() {
        const box = el('chat-picked-list');
        box.innerHTML = [...pickedMemberMap].map(([id, name]) => `
            <span class="chat-chip">${esc(name)}
                <button type="button" onclick="ChatBox.unpick(${id})"><i class="ph ph-x"></i></button>
            </span>`).join('');
        el('chat-picker-submit').disabled = pickedMemberMap.size === 0;
        el('chat-picked-count').textContent = pickedMemberMap.size ? `Đã chọn ${pickedMemberMap.size} người` : '';
    }

    async function submitMemberPicker() {
        const idList = [...pickedMemberMap.keys()];
        if (idList.length === 0) return;

        try {
            if (memberPickerTargetId === null) {
                const title = el('chat-group-title').value.trim();
                if (!title) { toast('Nhập tên nhóm trước đã.', 'error'); return; }
                const conversation = await API.post('/chat/group', { title, memberIdList: idList });
                closeMemberPicker();
                await refreshConversationList();
                openConversation(conversation.id);
            } else {
                const targetId = memberPickerTargetId;
                const result = await API.post(`/chat/group/${targetId}/member`, { memberIdList: idList });
                toast(result.message, 'success');
                closeMemberPicker();
                await refreshConversationList();
                openConversation(targetId);
            }
        } catch (e) { toast(e.message, 'error'); }
    }

    async function renameGroup() {
        if (!currentConversation) return;
        const title = await promptDialog('Tên mới của nhóm:', {
            title: 'Đổi tên nhóm', value: currentConversation.title, okText: 'Đổi tên',
            // Tên nhóm là MỘT dòng: khung ba dòng làm cả hộp thoại trông như đang đòi viết một đoạn văn,
            // mà Enter ở đó lại xuống dòng thay vì xác nhận. 200 khớp trần của Conversation.Title.
            singleLine: true, maxLength: 200
        });
        if (!title) return;                 // bấm Hủy, hoặc bỏ trống (promptDialog đã tự chặn chuỗi rỗng)

        try {
            await API.put(`/chat/group/${currentConversationId}`, { title });
            await refreshConversationList();
            currentConversation = conversationList.find(x => x.id === currentConversationId) ?? currentConversation;
            renderThreadHeader();
            toast('Đã đổi tên nhóm.', 'success');
        } catch (e) { toast(e.message, 'error'); }
    }

    async function showMemberList() {
        if (!currentConversationId) return;
        let list;
        try { list = await API.getSilent(`/chat/conversation/${currentConversationId}/member`); }
        catch (e) { toast(e.message, 'error'); return; }

        const canManage = currentConversation?.createdByUserId === me();
        const box = el('chat-member-panel');
        box.innerHTML = `
            <header>
                <b>Thành viên (${list.length})</b>
                <button type="button" onclick="ChatBox.closeMemberPanel()"><i class="ph ph-x"></i></button>
            </header>
            <div class="chat-group-head">
                <span class="chat-group-photo">
                    ${conversationAvatarHtml(currentConversation, 'lg')}
                    ${canManage
                        ? `<button type="button" class="chat-photo-btn" title="Đổi ảnh nhóm"
                               onclick="ChatBox.pickGroupAvatar()"><i class="ph-fill ph-camera"></i></button>`
                        : ''}
                </span>
                <span class="body">
                    <b>${esc(currentConversation?.title ?? '')}</b>
                    <small>${list.length} thành viên</small>
                </span>
                ${canManage && currentConversation?.avatarUrl
                    ? `<button type="button" class="chat-kick" title="Gỡ ảnh nhóm"
                           onclick="ChatBox.removeGroupAvatar()"><i class="ph ph-trash"></i></button>`
                    : ''}
            </div>
            <div class="chat-member-scroll">
                ${list.map(m => `
                    <div class="chat-conv">
                        ${avatarHtml(m.avatarUrl, m.fullName, onlineIdSet.has(m.userId))}
                        <span class="body">
                            <b>${esc(m.fullName)}${m.isOwner ? ' <em>(người lập nhóm)</em>' : ''}</b>
                            <small>${esc(m.departmentName ?? '')}</small>
                        </span>
                        ${canManage && !m.isOwner
                            ? `<button type="button" class="chat-kick" title="Mời ra khỏi nhóm"
                                   onclick="ChatBox.removeMember(${m.userId})"><i class="ph ph-user-minus"></i></button>`
                            : ''}
                    </div>`).join('')}
            </div>
            <footer>
                ${canManage
                    ? `<button type="button" class="chat-btn" onclick="ChatBox.addMember()"><i class="ph ph-user-plus"></i> Thêm thành viên</button>`
                    : ''}
                <button type="button" class="chat-btn danger" onclick="ChatBox.leaveGroup()"><i class="ph ph-sign-out"></i> Rời nhóm</button>
            </footer>`;
        box.classList.remove('hidden');
    }

    function closeMemberPanel() { el('chat-member-panel').classList.add('hidden'); }

    function addMember() {
        closeMemberPanel();
        openMemberPicker(currentConversationId);
    }

    function pickGroupAvatar() { el('chat-file-group-avatar').click(); }

    async function uploadGroupAvatar(file) {
        const form = new FormData();
        form.append('file', file);
        try {
            await API.postForm(`/chat/group/${currentConversationId}/avatar`, form);
            await reloadCurrentConversation();
            showMemberList();
            toast('Đã đổi ảnh nhóm.', 'success');
        } catch (e) { toast(e.message, 'error'); }
    }

    async function removeGroupAvatar() {
        if (!await confirmDialog('Gỡ ảnh nhóm, quay về biểu tượng mặc định?')) return;
        try {
            await API.del(`/chat/group/${currentConversationId}/avatar`);
            await reloadCurrentConversation();
            showMemberList();
            toast('Đã gỡ ảnh nhóm.', 'success');
        } catch (e) { toast(e.message, 'error'); }
    }

    /** Tải lại danh sách rồi lấy đúng hội thoại đang mở ra khỏi đó. Không gán lại currentConversation thì
     *  thanh tiêu đề vẫn cầm bản cũ và ảnh/tên vừa đổi không hiện lên cho tới lần mở sau. */
    async function reloadCurrentConversation() {
        await refreshConversationList();
        currentConversation = conversationList.find(x => x.id === currentConversationId) ?? currentConversation;
        renderThreadHeader();
    }

    async function removeMember(userId) {
        if (!await confirmDialog('Mời người này ra khỏi nhóm?')) return;
        try {
            await API.del(`/chat/group/${currentConversationId}/member/${userId}`);
            toast('Đã mời thành viên ra khỏi nhóm.', 'success');
            showMemberList();
        } catch (e) { toast(e.message, 'error'); }
    }

    async function leaveGroup() {
        if (!await confirmDialog('Rời khỏi nhóm này? Bạn sẽ không nhận được tin nhắn của nhóm nữa.')) return;
        try {
            await API.post(`/chat/group/${currentConversationId}/leave`);
            closeMemberPanel();
            back();
            await refreshConversationList();
            toast('Đã rời nhóm.', 'success');
        } catch (e) { toast(e.message, 'error'); }
    }

    // ==================== VẼ ====================

    /** Dòng xem trước trong danh sách hội thoại — khớp với ChatController.PreviewOf ở phía server, dùng cho
     *  những tin vừa nhận qua hub (server chỉ tính sẵn cho dữ liệu tải bằng REST). */
    function previewOf(m) {
        switch (m.messageType) {
            case 'Image': return '[Hình ảnh]';
            case 'Audio': return '[Tin nhắn thoại]';
            case 'File': return `[Tệp] ${m.attachmentName ?? ''}`;
            default: return m.content ?? '';
        }
    }

    function bodyHtml(m) {
        if (m.isDeleted) return '<div class="bubble"><i>Tin nhắn đã thu hồi</i></div>';

        switch (m.messageType) {
            case 'Sticker':
                return `<div class="chat-sticker">${esc(m.content)}</div>`;

            case 'Image':
                // rel="noopener": tab mở ra trỏ ngược về được window.opener của trang gốc nếu thiếu.
                return `<a class="chat-image" href="${esc(m.attachmentUrl)}" target="_blank" rel="noopener">
                            <img src="${esc(m.attachmentUrl)}" alt="${esc(m.attachmentName ?? 'Hình ảnh')}" loading="lazy">
                        </a>${captionHtml(m)}`;

            case 'Audio':
                return `<audio class="chat-audio" controls preload="metadata" src="${esc(m.attachmentUrl)}"></audio>`;

            case 'File': {
                // download: ép TẢI VỀ thay vì mở trong tab — và đặt lại đúng tên gốc, vì tệp trên đĩa mang tên
                // GUID (xem FileService.SaveAsync) nên không có thuộc tính này thì người ta tải về một chuỗi hex.
                const kind = fileKindOf(m.attachmentName);
                return `<a class="chat-file kind-${kind.key}" href="${esc(m.attachmentUrl)}" download="${esc(fileNameOf(m.attachmentName))}">
                            <span class="chat-file-icon"><i class="ph-fill ${kind.icon}"></i><em>${esc(kind.label)}</em></span>
                            <span class="body">
                                <b>${esc(m.attachmentName ?? 'Tệp đính kèm')}</b>
                                <small>${formatSize(m.attachmentSize)} · Bấm để tải về</small>
                            </span>
                            <i class="ph ph-download-simple chat-file-dl"></i>
                        </a>${captionHtml(m)}`;
            }

            default:
                return EMOJI_ONLY.test((m.content ?? '').trim())
                    ? `<div class="chat-emoji">${esc(m.content.trim())}</div>`
                    : `<div class="bubble">${esc(m.content)}</div>`;
        }
    }

    /** Ảnh và tệp có thể gửi kèm một dòng chữ — không có thì đừng vẽ bong bóng rỗng. */
    const captionHtml = (m) => m.content ? `<div class="bubble">${esc(m.content)}</div>` : '';

    /** Tên thư mục bị lược đi khi TẢI VỀ: trình duyệt không cho thuộc tính download tạo thư mục con, gặp dấu
     *  "/" nó tự thay bằng "_" và cho ra cái tên "Hồ sơ_2026_CV.pdf" khó đọc. */
    const fileNameOf = (name) => (name ?? 'tep-dinh-kem').split('/').pop();

    /** Loại tệp theo đuôi — mỗi loại một màu và một nhãn chữ trên ô biểu tượng, để liếc qua là phân biệt được
     *  tệp Word với PDF hay bảng tính mà không phải đọc tên tệp. */
    function fileKindOf(name) {
        const ext = (name ?? '').split('.').pop().toLowerCase();
        const kind = (key, icon, label) => ({ key, icon, label });
        if (['doc', 'docx'].includes(ext)) return kind('word', 'ph-file-doc', 'DOC');
        if (ext === 'pdf') return kind('pdf', 'ph-file-pdf', 'PDF');
        if (['xls', 'xlsx', 'csv'].includes(ext)) return kind('sheet', 'ph-file-xls', ext.toUpperCase());
        if (['ppt', 'pptx'].includes(ext)) return kind('slide', 'ph-file-ppt', 'PPT');
        if (['zip', 'rar', '7z'].includes(ext)) return kind('zip', 'ph-file-zip', ext.toUpperCase());
        if (['mp4', 'webm', 'mov'].includes(ext)) return kind('video', 'ph-file-video', ext.toUpperCase());
        return kind('other', 'ph-file', ext && ext.length <= 4 ? ext.toUpperCase() : 'TỆP');
    }

    const timeOf = (value) => new Date(value).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });

    /** Mốc giờ căn giữa giữa hai đoạn hội thoại, kiểu Messenger: hôm nay chỉ ghi giờ, hôm qua ghi "Hôm qua",
     *  trong tuần ghi thứ, xa hơn thì ghi cả ngày. */
    function stampLabel(value) {
        const at = new Date(value);
        const today = new Date();
        const hm = timeOf(value);
        if (at.toDateString() === today.toDateString()) return hm;

        const yesterday = new Date(today);
        yesterday.setDate(today.getDate() - 1);
        if (at.toDateString() === yesterday.toDateString()) return `Hôm qua ${hm}`;

        if (today - at < 7 * 24 * 60 * 60 * 1000) {
            const weekday = at.toLocaleDateString('vi-VN', { weekday: 'long' });
            return `${weekday.charAt(0).toUpperCase()}${weekday.slice(1)} ${hm}`;
        }
        return `${hm}, ${at.toLocaleDateString('vi-VN')}`;
    }

    /** Hai tin cách nhau quá chừng này thì chèn một mốc giờ ở giữa — như Messenger, giờ không đi theo từng
     *  tin mà chỉ hiện ở chỗ cuộc nói chuyện bị ngắt quãng. */
    const STAMP_GAP_MS = 15 * 60 * 1000;

    /** Tin chỉ gồm 1–3 emoji thì hiện to, không bong bóng — đúng kiểu Messenger. */
    const EMOJI_ONLY = /^(?:\p{Extended_Pictographic}[\u{1F3FB}-\u{1F3FF}\u{FE0F}\u{200D}\p{Extended_Pictographic}]*\s*){1,3}$/u;

    /** Khoảng lặng đủ lâu thì cắt cụm, dù vẫn là một người nói. Không có nó thì tin nhắn hôm qua và tin
     *  nhắn sáng nay của cùng một người dính liền thành một khối, trông như họ vừa nói một mạch. */
    const RUN_GAP_MS = 5 * 60 * 1000;

    /** Tin này có MỞ đầu một cụm không? Chỉ tin đầu cụm mới đeo avatar và hiện tên người gửi — đeo cho
     *  mọi tin thì một người nói liền năm câu là năm cái mặt xếp dọc, đúng thứ Messenger/Zalo tránh. */
    function startsRun(m, previous) {
        if (!previous) return true;
        if (Number(previous.senderUserId) !== m.senderUserId) return true;
        const at = new Date(m.createdAt), previousAt = new Date(previous.createdAt);
        // Sang ngày mới luôn mở cụm mới, dù chỉ cách vài phút (23:58 → 00:01): giữa hai tin có vạch chia ngày,
        // để cụm chạy xuyên qua vạch thì cụm bên dưới mất avatar còn cụm bên trên mất giờ gửi.
        if (at.toDateString() !== previousAt.toDateString()) return true;
        return Math.abs(at - previousAt) > RUN_GAP_MS;
    }

    /** @param previous tin liền trước (hoặc {senderUserId, createdAt} đọc lại từ DOM), null nếu là tin đầu.
     *  @param next tin liền sau, null nếu đây là tin cuối — tin cuối cụm mới mang giờ gửi; với tin vừa tới qua
     *  hub thì luôn là tin cuối, appendMessage tự gỡ dấu "cuối cụm" khỏi tin trước nó. */
    function messageHtml(m, previous, next = null) {
        const mine = m.senderUserId === me();
        const isGroup = currentConversation?.type === 'Group';
        const opensRun = startsRun(m, previous);
        const closesRun = !next || startsRun(next, m);
        // Mốc giờ căn giữa thay cho việc in giờ dưới MỌI tin: chỉ chèn ở tin đầu tiên, khi sang ngày mới, hoặc
        // khi cuộc nói chuyện ngắt quãng lâu. Giờ chính xác của từng tin vẫn xem được khi rê chuột (title).
        const showStamp = !previous
            || new Date(previous.createdAt).toDateString() !== new Date(m.createdAt).toDateString()
            || Math.abs(new Date(m.createdAt) - new Date(previous.createdAt)) > STAMP_GAP_MS;

        // Tin của chính mình KHÔNG đeo avatar — giống Messenger: nửa bên phải thì đương nhiên là mình.
        // Avatar người kia đeo ở tin CUỐI cụm, nằm sát đáy (đúng như Messenger); các tin khác trong cụm chèn một
        // ô trống đúng bề ngang avatar để cả cụm thẳng hàng.
        const avatar = mine
            ? ''
            : (closesRun
                ? avatarHtml(m.senderAvatarUrl, m.senderName, false, 'sm')
                : '<span class="chat-avatar-gap"></span>');

        // data-sender / data-at để appendMessage biết tin liền trước là của ai và vào lúc nào mà quyết định
        // cắt cụm — lúc tin mới tới qua hub thì không còn danh sách nào trong tay, chỉ còn DOM.
        // Nút thu hồi nằm NGOÀI dòng giờ: giờ chỉ còn hiện ở tin cuối cụm, mà tin nào của mình cũng phải thu
        // hồi được. Nó đứng cạnh bong bóng và chỉ hiện khi rê chuột (xem .chat-recall trong style.css).
        const recall = mine && !m.isDeleted
            ? `<button type="button" class="chat-recall" title="Thu hồi"
                   onclick="ChatBox.recall(${m.id})"><i class="ph ph-arrow-counter-clockwise"></i></button>`
            : '';

        return `${showStamp ? `<div class="chat-stamp">${esc(stampLabel(m.createdAt))}</div>` : ''}
        <div class="chat-msg ${mine ? 'mine' : ''}${opensRun ? ' starts-run' : ''}${closesRun ? ' ends-run' : ''}"
                     data-msg="${m.id}" data-sender="${m.senderUserId}" data-at="${esc(m.createdAt)}">
            ${avatar}
            ${recall}
            <div class="chat-col">
                ${!mine && isGroup && opensRun ? `<span class="who">${esc(m.senderName)}</span>` : ''}
                <div class="chat-body" title="${esc(formatDate(m.createdAt, true))}">${bodyHtml(m)}</div>
            </div>
        </div>`;
    }

    async function recall(messageId) {
        if (!await confirmDialog('Thu hồi tin nhắn này?')) return;
        try { await API.post(`/chat/message/${messageId}/delete`); }
        catch (e) { toast(e.message, 'error'); }
    }

    function appendMessage(m) {
        const box = el('chat-message-list');
        box.querySelector('.chat-hello')?.remove();       // lời chào của hội thoại trống nhường chỗ cho tin đầu tiên
        const last = box.lastElementChild;
        const previous = last?.dataset.sender
            ? { senderUserId: last.dataset.sender, createdAt: last.dataset.at }
            : null;

        // Tin mới nối tiếp cụm của tin trước thì tin trước không còn là "cuối cụm": avatar (đeo ở tin cuối cụm)
        // dời xuống tin mới, chỗ cũ thay bằng ô trống cùng bề ngang.
        if (previous && !startsRun(m, previous)) {
            last.classList.remove('ends-run');
            last.querySelector(':scope > .chat-avatar')?.replaceWith(
                Object.assign(document.createElement('span'), { className: 'chat-avatar-gap' }));
        }

        box.insertAdjacentHTML('beforeend', messageHtml(m, previous));
        box.lastElementChild.classList.add('is-new');
        scrollToBottom();
    }

    function scrollToBottom() {
        const box = el('chat-message-list');
        box.scrollTop = box.scrollHeight;
    }

    function avatarHtml(url, name, isOnline, extraClass = '') {
        return `<span class="chat-avatar${extraClass ? ` ${extraClass}` : ''}${isOnline ? ' online' : ''}">${url
            ? `<img src="${esc(url)}" alt="">`
            : esc((name || '?').charAt(0).toUpperCase())}</span>`;
    }

    /** Ảnh đại diện của một hội thoại, dùng chung cho danh sách bên trái, thanh tiêu đề và bảng thành viên.
     *  Nhóm đã đặt ảnh thì dùng ảnh đó; nhóm chưa đặt thì hiện biểu tượng nhóm thay vì chữ cái đầu của tên
     *  nhóm — một chữ "T" tròn trịa trông y hệt avatar của một người tên Tuấn. */
    function conversationAvatarHtml(conversation, extraClass = '') {
        if (!conversation) return '';
        if (conversation.type === 'Group' && !conversation.avatarUrl)
            return `<span class="chat-avatar group${extraClass ? ` ${extraClass}` : ''}"><i class="ph-fill ph-users-three"></i></span>`;

        const isOnline = conversation.type !== 'Group'
            && conversation.partnerUserId && onlineIdSet.has(conversation.partnerUserId);
        return avatarHtml(conversation.avatarUrl, conversation.title, isOnline, extraClass);
    }

    function renderThreadHeader() {
        const conversation = currentConversation;
        el('chat-thread-title').textContent = conversation?.title ?? '';
        el('chat-thread-avatar').innerHTML = conversationAvatarHtml(conversation, 'md');
        applyBackground(conversation?.backgroundKey, conversation?.backgroundUrl);

        const isGroup = conversation?.type === 'Group';
        const isOnline = !isGroup && conversation?.partnerUserId && onlineIdSet.has(conversation.partnerUserId);
        const sub = el('chat-thread-sub');
        sub.textContent = isGroup ? `${conversation.memberCount} thành viên` : (isOnline ? 'Đang hoạt động' : 'Ngoại tuyến');
        sub.classList.toggle('online', !!isOnline);
        el('chat-group-btn').classList.toggle('hidden', !isGroup);
        el('chat-rename-btn').classList.toggle('hidden', !isGroup || conversation.createdByUserId !== me());
        // CỐ Ý không đóng bảng thành viên ở đây. Mời một người ra khỏi nhóm sẽ khiến server đẩy về
        // "ConversationChanged", mà tín hiệu đó lại chạy qua đúng hàm này — đóng ở đây thì bảng thành viên
        // vừa được vẽ lại xong đã bị sập ngay trước mắt người đang đứng quản lý nhóm.
    }

    function setFilter(filter) {
        listFilter = filter;
        el('chat-filter').querySelectorAll('button')
            .forEach(node => node.classList.toggle('active', node.dataset.filter === filter));
        renderConversationList();
    }

    /** Hội thoại ĐANG MỞ luôn được giữ lại trong danh sách, kể cả khi nó không còn khớp bộ lọc.
     *
     *  Chuyện này chỉ thành vấn đề từ khi hai cột cùng hiện một lúc: đang lọc "Chưa đọc" mà bấm vào một
     *  dòng thì hệ thống đánh dấu đã đọc ngay, và dòng đó biến mất khỏi cột trái NGAY TRONG LÚC người ta
     *  đang đọc nó ở cột phải — cột trái không còn cho biết mình đang đứng ở đâu. */
    const filteredConversationList = () =>
        listFilter === 'unread'
            ? conversationList.filter(x => x.unreadCount > 0 || x.id === currentConversationId)
            : listFilter === 'group' ? conversationList.filter(x => x.type === 'Group')
                : conversationList;

    /** Lời nhắn khi danh sách rỗng phải nói đúng LÝ DO rỗng. Dùng chung một câu "chưa có cuộc trò chuyện
     *  nào" cho cả ba bộ lọc thì người đang lọc "Chưa đọc" sẽ tưởng mình vừa mất sạch tin nhắn. */
    function emptyListHtml() {
        if (listFilter === 'unread') return '<div class="chat-empty">Bạn đã đọc hết tin nhắn.</div>';
        if (listFilter === 'group') return '<div class="chat-empty">Bạn chưa ở trong nhóm nào.<br>Bấm biểu tượng nhóm ở góc trên để lập một nhóm.</div>';
        return '<div class="chat-empty">Chưa có cuộc trò chuyện nào.<br>Gõ tên đồng nghiệp ở ô trên để bắt đầu.</div>';
    }

    function renderConversationList() {
        el('chat-conversation-list').innerHTML = filteredConversationList().map(c => `
            <button type="button" data-conv="${c.id}"
                    class="chat-conv${c.id === currentConversationId ? ' current' : ''}${c.unreadCount ? ' unread' : ''}"
                    onclick="ChatBox.open(${c.id})">
                ${conversationAvatarHtml(c)}
                <span class="body">
                    <!-- "line" chứ không phải "row": .row là lớp bố cục biểu mẫu chung trong style.css (con được
                         flex:1 + min-width:180px), dính vào đây là ngày tháng bị đẩy xuống dòng và số tin chưa
                         đọc bị kéo thành một thanh đỏ dài. -->
                    <span class="line">
                        <b>${esc(c.title)}</b>
                        <em>${shortTime(c.lastMessageAt)}</em>
                    </span>
                    <span class="line">
                        <small>${c.lastMessageIsMine ? 'Bạn: ' : ''}${esc(c.lastMessage ?? 'Chưa có tin nhắn')}</small>
                        ${c.unreadCount ? `<span class="n">${c.unreadCount > 99 ? '99+' : c.unreadCount}</span>` : ''}
                    </span>
                </span>
            </button>`).join('') || emptyListHtml();

        // Huy hiệu trên tab "Chưa đọc" đếm theo SỐ HỘI THOẠI còn tin chưa đọc, không phải tổng số tin:
        // tab này lọc ra các dòng, nên con số phải khớp đúng số dòng người ta sắp nhìn thấy.
        const unreadTab = el('chat-filter').querySelector('[data-filter="unread"]');
        const count = conversationList.filter(x => x.unreadCount > 0).length;
        unreadTab.dataset.count = count || '';
    }

    /** Mốc thời gian gọn cho danh sách hội thoại: trong ngày thì hiện giờ, hôm qua thì nói "Hôm qua", xa
     *  hơn thì ngày/tháng. Dùng formatDate đầy đủ ở đây là một chuỗi dài nuốt mất chỗ của tên người. */
    function shortTime(value) {
        if (!value) return '';
        const at = new Date(value);
        const now = new Date();

        if (at.toDateString() === now.toDateString())
            return at.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });

        const yesterday = new Date(now);
        yesterday.setDate(now.getDate() - 1);
        if (at.toDateString() === yesterday.toDateString()) return 'Hôm qua';

        return at.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' });
    }

    function renderStickerPanel() {
        el('chat-sticker-panel').innerHTML = STICKER_GROUP_LIST.map(group => `
            <div class="chat-sticker-group">
                <small>${esc(group.name)}</small>
                <div>${group.list.map(emoji =>
                    `<button type="button" onclick="ChatBox.sendSticker('${emoji}')">${emoji}</button>`).join('')}</div>
            </div>`).join('');
    }

    function toggleStickerPanel() {
        const panel = el('chat-sticker-panel');
        if (!panel.innerHTML) renderStickerPanel();
        closeBackgroundPanel();                   // hai bảng cùng chiếm một chỗ ngay trên ô nhập
        panel.classList.toggle('hidden');
    }

    function closeStickerPanel() { el('chat-sticker-panel').classList.add('hidden'); }

    /** Trả về false nếu trang này không có thanh trên để gắn vào. */
    function render() {
        // Nút bấm + huy hiệu đã nằm sẵn trong thanh trên do mountLayout dựng (xem app.js); ở đây chỉ đổ
        // thêm BẢNG chat vào trong thẻ bọc đó, để nó xổ xuống ngay dưới nút y như chuông thông báo.
        const anchor = el('chat-bell');
        if (!anchor) return false;

        anchor.insertAdjacentHTML('beforeend', `
        <div id="chat-widget">
            <div id="chat-panel" class="hidden">

                <!-- ==================== CỘT TRÁI: DANH SÁCH ==================== -->
                <aside class="chat-rail">
                    <header class="chat-rail-head">
                        <b>Tin nhắn</b>
                        <button type="button" title="Nhóm mới" onclick="ChatBox.newGroup()">
                            <i class="ph ph-users-three"></i>
                        </button>
                        <button type="button" title="Đóng" onclick="ChatBox.toggle()">
                            <i class="ph ph-x"></i>
                        </button>
                    </header>

                    <div class="chat-search-box">
                        <i class="ph ph-magnifying-glass"></i>
                        <input id="chat-search" type="search" autocomplete="off" placeholder="Tìm người để nhắn...">
                    </div>

                    <!-- Lọc nhanh. Danh sách hội thoại dài dần theo tháng, mà thứ người ta mở hộp chat để
                         tìm gần như luôn là "cái nào chưa đọc" — bắt cuộn cả danh sách để tìm ba dòng in
                         đậm là bắt làm việc của máy. -->
                    <div class="chat-filter" id="chat-filter">
                        <button type="button" class="active" data-filter="all" onclick="ChatBox.setFilter('all')">Tất cả</button>
                        <button type="button" data-filter="unread" onclick="ChatBox.setFilter('unread')">Chưa đọc</button>
                        <button type="button" data-filter="group" onclick="ChatBox.setFilter('group')">Nhóm</button>
                    </div>

                    <div id="chat-contact-list" class="hidden"></div>
                    <div id="chat-conversation-list"></div>
                </aside>

                <!-- ==================== CỘT PHẢI: HỘI THOẠI ==================== -->
                <section class="chat-main">
                    <!-- Hai cột cùng hiện một lúc nên cột phải phải có thứ gì đó lúc chưa chọn ai. Bỏ
                         trống thì nửa bảng là một mảng trắng trông như đang lỗi. -->
                    <div id="chat-blank">
                        <span class="art"><i class="ph ph-chats-circle"></i></span>
                        <b>Chưa mở cuộc trò chuyện nào</b>
                        <small>Chọn một người ở cột bên trái, hoặc gõ tên đồng nghiệp vào ô tìm kiếm để bắt đầu.</small>
                        <!-- Hai lối bắt đầu ngay tại chỗ: người mới mở hộp chat lần đầu không phải đi dò xem ô tìm
                             kiếm hay nút lập nhóm nằm ở đâu. -->
                        <span class="chat-blank-actions">
                            <button type="button" class="primary" onclick="document.getElementById('chat-search').focus()">
                                <i class="ph ph-pencil-simple-line"></i> Nhắn tin mới</button>
                            <button type="button" onclick="ChatBox.newGroup()">
                                <i class="ph ph-users-three"></i> Tạo nhóm</button>
                        </span>
                    </div>

                    <div id="chat-thread">
                        <header class="chat-thread-head">
                            <!-- Nút quay lại chỉ có ý nghĩa trên màn hình hẹp, nơi hai cột xếp chồng lên
                                 nhau. Màn hình rộng thì danh sách luôn nằm đó, CSS giấu nút này đi. -->
                            <button type="button" class="chat-back" title="Danh sách" onclick="ChatBox.back()">
                                <i class="ph ph-arrow-left"></i>
                            </button>
                            <span id="chat-thread-avatar"></span>
                            <span class="chat-thread-name">
                                <b id="chat-thread-title"></b>
                                <small id="chat-thread-sub"></small>
                            </span>
                            <button type="button" id="chat-bg-btn" title="Đổi nền khung chat"
                                    onclick="ChatBox.toggleBackground()"><i class="ph ph-paint-brush"></i></button>
                            <button type="button" id="chat-rename-btn" class="hidden" title="Đổi tên nhóm"
                                    onclick="ChatBox.renameGroup()"><i class="ph ph-pencil-simple"></i></button>
                            <button type="button" id="chat-group-btn" class="hidden" title="Thành viên nhóm"
                                    onclick="ChatBox.showMemberList()"><i class="ph ph-users"></i></button>
                        </header>

                        <div id="chat-message-list"></div>
                        <div id="chat-member-panel" class="hidden"></div>
                        <div id="chat-typing" class="hidden" aria-label="Đang nhập..." title="Đang nhập..."><span class="dots"><i></i><i></i><i></i></span></div>
                        <div id="chat-upload" class="hidden"></div>

                        <div id="chat-record-bar" class="hidden">
                            <span class="dot"></span>
                            <span id="chat-record-time">00:00</span>
                            <button type="button" onclick="ChatBox.cancelRecord()">Hủy</button>
                            <button type="button" class="primary" onclick="ChatBox.stopRecord()">Gửi</button>
                        </div>

                        <div id="chat-sticker-panel" class="hidden"></div>
                        <div id="chat-bg-panel" class="hidden"></div>

                        <!-- Ô nhập, thanh công cụ và nút gửi nằm TRONG CÙNG một khung viền bo tròn, để cả
                             cụm đọc ra là "chỗ soạn tin" thay vì ba dải rời nhau. Ô nhập là <textarea>:
                             server nhận tới 4000 ký tự, mà một dòng đơn thì gõ quá ba câu đã không còn
                             nhìn thấy đầu tin mình vừa viết. -->
                        <form id="chat-form">
                            <div id="chat-tool-bar">
                                <button type="button" title="Gửi ảnh" onclick="ChatBox.pick('image')"><i class="ph ph-image"></i></button>
                                <button type="button" title="Đính kèm tệp" onclick="ChatBox.pick('file')"><i class="ph ph-paperclip"></i></button>
                                <button type="button" title="Đính kèm thư mục" onclick="ChatBox.pick('folder')"><i class="ph ph-folder-plus"></i></button>
                                <button type="button" title="Nhãn dán" onclick="ChatBox.toggleSticker()"><i class="ph ph-smiley"></i></button>
                                <button type="button" id="chat-mic-btn" title="Ghi âm" onclick="ChatBox.toggleRecord()"><i class="ph ph-microphone"></i></button>
                            </div>
                            <div class="chat-input-row">
                                <textarea id="chat-input" rows="1" maxlength="4000" autocomplete="off"
                                          placeholder="Aa" aria-label="Nhập tin nhắn"></textarea>
                                <button type="submit" id="chat-send-btn" title="Gửi (Enter)" disabled>
                                    <i class="ph-fill ph-paper-plane-right"></i>
                                </button>
                            </div>
                        </form>
                    </div>
                </section>

                <!-- ==================== LỚP PHỦ: LẬP NHÓM / MỜI THÊM ====================
                     Phủ lên CẢ hai cột thay vì là một khung thứ ba trượt ngang như trước. Nhờ vậy đóng nó
                     lại là mọi thứ phía sau còn nguyên chỗ cũ — không phải nhớ xem lúc mở nó ra thì người
                     dùng đang đứng ở danh sách hay đang đứng trong một hội thoại. -->
                <div id="chat-picker" class="hidden">
                    <header>
                        <button type="button" class="chat-back-always" onclick="ChatBox.closePicker()">
                            <i class="ph ph-arrow-left"></i>
                        </button>
                        <b id="chat-picker-heading">Nhóm mới</b>
                    </header>
                    <div class="chat-picker-body">
                        <div id="chat-group-title-box" class="chat-search-box">
                            <i class="ph ph-textbox"></i>
                            <input id="chat-group-title" maxlength="200" autocomplete="off" placeholder="Tên nhóm...">
                        </div>
                        <div class="chat-search-box">
                            <i class="ph ph-magnifying-glass"></i>
                            <input id="chat-member-search" type="search" autocomplete="off" placeholder="Tìm người để thêm...">
                        </div>
                        <div id="chat-picked-list"></div>
                        <div id="chat-member-result"></div>
                    </div>
                    <footer class="chat-picker-footer">
                        <small id="chat-picked-count"></small>
                        <button type="button" class="chat-btn" onclick="ChatBox.closePicker()">Hủy</button>
                        <button type="button" id="chat-picker-submit" class="chat-btn primary" disabled
                                onclick="ChatBox.submitPicker()">Lập nhóm</button>
                    </footer>
                </div>

                <!-- Các ô chọn tệp nằm ẩn: nút trên thanh công cụ chỉ việc bấm hộ. Dùng ô THẬT chứ không
                     dựng input tạm mỗi lần bấm, vì input tạo bằng script rồi bỏ đi ngay có thể bị trình
                     duyệt coi là thao tác không do người dùng khởi xướng và chặn hộp thoại chọn tệp. -->
                <input id="chat-file-image" type="file" accept="image/*" multiple hidden>
                <input id="chat-file-any" type="file" multiple hidden>
                <input id="chat-file-folder" type="file" webkitdirectory directory multiple hidden>
                <input id="chat-file-group-avatar" type="file" accept="image/*" hidden>
                <input id="chat-file-background" type="file" accept="image/*" hidden>
            </div>
        </div>`);

        el('chat-topbar-btn').addEventListener('click', toggle);
        el('chat-form').addEventListener('submit', e => { e.preventDefault(); send(); });

        const input = el('chat-input');
        input.addEventListener('input', syncComposer);
        input.addEventListener('keydown', e => {
            // Enter gửi, Shift+Enter xuống dòng — quy ước chung của mọi hộp chat.
            //
            // isComposing là chỗ BẮT BUỘC phải kiểm: bộ gõ tiếng Việt (và cả tiếng Nhật/Trung) dựng chữ qua
            // một phiên soạn thảo, mà Enter trong lúc đó là phím CHỐT chữ đang gõ dở chứ không phải lệnh gửi.
            // Bỏ qua nó thì mỗi lần chốt dấu là một tin nhắn cụt bay đi.
            if (e.key !== 'Enter' || e.shiftKey || e.isComposing) return;
            e.preventDefault();
            send();
        });

        // Ô chọn tệp: gửi xong PHẢI xóa value, không thì chọn lại đúng tệp vừa gửi sẽ không kích hoạt
        // sự kiện change (trình duyệt thấy giá trị không đổi) và cú bấm thứ hai im lặng không làm gì.
        const bindPicker = (id, messageType, nameOf) => el(id).addEventListener('change', async e => {
            const fileList = [...e.target.files];
            e.target.value = '';
            await sendFileList(fileList, messageType, nameOf);
        });
        bindPicker('chat-file-image', 'Image');
        bindPicker('chat-file-any', 'File');
        // webkitRelativePath là thứ DUY NHẤT còn giữ được cấu trúc thư mục người ta vừa chọn — file.name chỉ
        // là tên trần, nên gửi 3 tệp "index.html" từ 3 thư mục con sẽ ra ba dòng giống hệt nhau.
        bindPicker('chat-file-folder', 'File', f => f.webkitRelativePath || f.name);

        el('chat-file-group-avatar').addEventListener('change', async e => {
            const file = e.target.files[0];
            e.target.value = '';
            if (file) await uploadGroupAvatar(file);
        });

        el('chat-file-background').addEventListener('change', async e => {
            const file = e.target.files[0];
            e.target.value = '';
            if (file) await uploadBackgroundImage(file);
        });

        // Bấm ra ngoài thì đóng — giống hệt chuông thông báo. Bảng nằm TRONG thẻ bọc nên bấm vào chính
        // nó vẫn tính là bấm bên trong, không tự đóng khi đang gõ dở.
        document.addEventListener('click', e => {
            // Hộp thoại xác nhận (thu hồi tin, rời nhóm...) do app.js gắn thẳng vào <body> nên nằm NGOÀI thẻ
            // bọc này. Không chừa nó ra thì cú bấm "Đồng ý" vừa xác nhận vừa đóng sập luôn bảng chat, và
            // người dùng mất chỗ đang đứng sau mỗi thao tác.
            if (e.target.closest('.modal-mask')) return;

            // Nút vừa bấm có thể đã bị chính đoạn xử lý của nó GỠ KHỎI DOM trước khi cú bấm kịp nổi bọt
            // lên tới đây — chuyện xảy ra mỗi lần một bảng con vẽ lại chính mình (chọn nền, bỏ một người
            // khỏi danh sách mời...). Nút đã rời DOM thì không còn nằm trong thẻ bọc nào nữa, nên
            // contains() trả về false và cả hộp chat tự đóng, dù người ta bấm đúng vào nút bên trong nó.
            if (!e.target.isConnected) return;

            if (!anchor.contains(e.target)) el('chat-panel').classList.add('hidden');
        });

        bindContactSearch();
        bindMemberSearch();
        syncComposer();          // đặt đúng chiều cao một dòng ngay từ đầu, đừng đợi phím đầu tiên
        return true;
    }

    /** Tìm người: gõ xong 300ms mới gọi API, không phải mỗi phím một request. */
    function debounceSearch(inputId, handler) {
        let timer;
        el(inputId).addEventListener('input', e => {
            clearTimeout(timer);
            const keyword = e.target.value.trim();
            timer = setTimeout(() => handler(keyword), 300);
        });
    }

    function bindContactSearch() {
        const box = el('chat-contact-list');
        debounceSearch('chat-search', async keyword => {
            if (!keyword) { box.classList.add('hidden'); return; }

            let list;
            try { list = await API.getSilent(`/chat/contact?keyword=${encodeURIComponent(keyword)}`); }
            catch { return; }

            box.classList.remove('hidden');
            box.innerHTML = list.map(u => `
                <button type="button" class="chat-conv" onclick="ChatBox.startDirect(${u.id})">
                    ${avatarHtml(u.avatarUrl, u.fullName, onlineIdSet.has(u.id))}
                    <span class="body">
                        <b>${esc(u.fullName)}</b>
                        <small>${esc(u.departmentName ?? u.employeeCode)}</small>
                    </span>
                </button>`).join('') || '<div class="chat-empty">Không tìm thấy ai.</div>';
        });
    }

    function bindMemberSearch() {
        const box = el('chat-member-result');
        debounceSearch('chat-member-search', async keyword => {
            if (!keyword) { box.innerHTML = '<div class="chat-empty">Gõ tên đồng nghiệp để tìm.</div>'; return; }

            let list;
            try { list = await API.getSilent(`/chat/contact?keyword=${encodeURIComponent(keyword)}`); }
            catch { return; }

            box.innerHTML = list.map(u => `
                <button type="button" class="chat-conv pickable${pickedMemberMap.has(u.id) ? ' picked' : ''}"
                        data-uid="${u.id}" data-name="${esc(u.fullName)}"
                        onclick="ChatBox.pickFrom(this)">
                    ${avatarHtml(u.avatarUrl, u.fullName, onlineIdSet.has(u.id))}
                    <span class="body">
                        <b>${esc(u.fullName)}</b>
                        <small>${esc(u.departmentName ?? u.employeeCode)}</small>
                    </span>
                    <i class="ph ph-check-circle tick"></i>
                </button>`).join('') || '<div class="chat-empty">Không tìm thấy ai.</div>';
        });
    }

    function toggle() {
        const panel = el('chat-panel');
        const willOpen = panel.classList.contains('hidden');

        // Mấy bảng cùng xổ ra từ những cái nút sát nhau trên thanh trên thì chúng đè lên nhau — mở cái này
        // là mọi cái khác phải tắt. Việc đó do app.js lo tập trung một chỗ (xem closeTopbarOverlay).
        if (willOpen && typeof closeTopbarOverlay === 'function') closeTopbarOverlay('chat');

        panel.classList.toggle('hidden');
        if (willOpen && !currentConversationId) el('chat-search').focus({ preventScroll: true });
        // Đóng bảng trong lúc còn đang thu tiếng thì micro vẫn sáng đèn mà không còn chỗ nào bấm dừng.
        if (!willOpen) cancelRecord();
    }

    /** Đóng hẳn bảng chat. Tách riêng khỏi toggle() để closeTopbarOverlay bên app.js gọi được mà không sợ
     *  vô tình MỞ nó ra khi nó đang đóng sẵn. */
    function closePanel() {
        el('chat-panel')?.classList.add('hidden');
        cancelRecord();
    }

    function back() {
        currentConversationId = null;
        currentConversation = null;
        closeStickerPanel();
        closeBackgroundPanel();
        closeMemberPanel();
        cancelRecord();
        el('chat-widget').classList.remove('thread-open');
        renderConversationList();        // bỏ tô sáng dòng vừa đóng
    }

    return {
        init,
        toggle,
        close: closePanel,
        back,
        open: openConversation,
        setFilter,
        startDirect,
        recall,

        // Đính kèm
        pick: (kind) => el({ image: 'chat-file-image', file: 'chat-file-any', folder: 'chat-file-folder' }[kind]).click(),
        toggleSticker: toggleStickerPanel,
        toggleBackground: toggleBackgroundPanel,
        setBackground,
        pickBackgroundImage,
        sendSticker,
        toggleRecord,
        stopRecord,
        cancelRecord,

        // Nhóm
        newGroup: () => openMemberPicker(null),
        closePicker: closeMemberPicker,
        pickFrom: pickFromNode,
        unpick: (userId) => { pickedMemberMap.delete(userId); renderPickedMemberList(); syncPickedTick(userId); },
        submitPicker: submitMemberPicker,
        renameGroup,
        showMemberList,
        closeMemberPanel,
        addMember,
        removeMember,
        leaveGroup,
        pickGroupAvatar,
        removeGroupAvatar
    };
})();
