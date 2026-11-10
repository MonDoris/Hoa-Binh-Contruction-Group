// File: wwwroot/pages/ai-tutor/ai-tutor.js
(async function () {
    await Lang.ready;

    if (!Auth.require(['Học viên', 'Trưởng Nhóm', 'Giảng Viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'], 'ai-tutor.view')) return;

    // Dựng khung hệ thống: cột chức năng bên trái, thanh trên, chuông thông báo, điểm/streak. Thiếu dòng này
    // thì trang chat trở thành một ốc đảo — mở HB Minerva xong không còn đường nào quay lại phần khác của hệ
    // thống ngoài nút Back của trình duyệt.
    mountLayout('ai-tutor',
        t('aitutor.title', 'HB Minerva'),
        t('aitutor.subtitle', 'Mình là trợ lý AI dễ thương nhất công ty'));

    const LAST_SESSION_KEY = 'hbe_aitutor_session';

    // Giới hạn tệp đính kèm — khớp AiTutorService (đổi bên đó phải đổi cả ở đây). Máy chủ vẫn kiểm tra lại.
    const MAX_FILES = 3;
    const MAX_FILE_BYTE = 10 * 1024 * 1024;
    const MAX_QUESTION = 500;
    const IMAGE_EXT = ['png', 'jpg', 'jpeg', 'webp'];
    const DOC_EXT = ['pdf', 'docx', 'txt', 'md'];
    // Dòng đầu tin nhắn người dùng khi có tệp, do AiTutorService ghi: "[Đính kèm: a.pdf; b.png]".
    const ATTACH_MARKER = /^\[Đính kèm: ([^\]]*)\]\n/;

    const logBox     = document.getElementById('chat-log');
    const scrollArea = document.getElementById('scroll-area');
    const inputBox   = document.getElementById('f-question');
    const btnSend    = document.getElementById('btn-send');
    const btnStop    = document.getElementById('btn-stop');
    const btnNew     = document.getElementById('btn-new');
    const btnClearAll = document.getElementById('btn-clear-all');
    const historyBox = document.getElementById('history-list');
    const timerBox   = document.getElementById('ask-timer');
    const desk       = document.getElementById('desk');
    const tray       = document.getElementById('tray');
    const qCount     = document.getElementById('q-count');
    const dropVeil   = document.getElementById('drop-veil');
    const inImage    = document.getElementById('in-image');
    const inFile     = document.getElementById('in-file');
    const btnPickImage = document.getElementById('btn-pick-image');
    const btnPickFile  = document.getElementById('btn-pick-file');

    let askController = null;
    let isReady = false;
    let turnCount = 0;
    let sessionId = null;
    let sessionList = [];
    let pendingFileList = [];   // [{ file, url }] — url là ảnh xem trước, thu hồi khi gỡ / gửi

    renderWelcome();
    bindEvent();
    await loadStatus();
    await loadSessionList();

    const lastId = Number(localStorage.getItem(LAST_SESSION_KEY)) || 0;
    if (lastId && sessionList.some(x => x.id === lastId)) await openSession(lastId);

    // ==================== SỰ KIỆN ====================
    function bindEvent() {
        btnSend.addEventListener('click', ask);
        btnStop.addEventListener('click', () => askController?.abort());
        btnNew.addEventListener('click', newConversation);
        btnClearAll.addEventListener('click', deleteAllSession);

        inputBox.addEventListener('input', () => {
            autoGrow();
            refreshSendButton();
        });

        inputBox.addEventListener('keydown', (e) => {
            if (e.key !== 'Enter' || e.shiftKey || e.isComposing) return;
            e.preventDefault();
            ask();
        });

        // Dán ảnh (chụp màn hình rồi Ctrl+V) thẳng vào ô gõ
        inputBox.addEventListener('paste', (e) => {
            const pasted = [...(e.clipboardData?.files ?? [])];
            if (pasted.length === 0) return;
            e.preventDefault();
            addFiles(pasted);
        });

        btnPickImage.addEventListener('click', () => inImage.click());
        btnPickFile.addEventListener('click', () => inFile.click());
        for (const input of [inImage, inFile]) {
            input.addEventListener('change', () => { addFiles([...input.files]); input.value = ''; });
        }

        // Kéo-thả tệp vào cả khung chính. dragCount đếm vào/ra để lớp phủ không nhấp nháy khi chuột đi qua phần tử con.
        const main = document.getElementById('ai-main');
        let dragCount = 0;
        const hasFiles = (e) => [...(e.dataTransfer?.types ?? [])].includes('Files');
        main.addEventListener('dragenter', (e) => { if (!hasFiles(e)) return; e.preventDefault(); dragCount++; showDrop(true); });
        main.addEventListener('dragover',  (e) => { if (hasFiles(e)) e.preventDefault(); });
        main.addEventListener('dragleave', (e) => { if (!hasFiles(e)) return; dragCount = Math.max(0, dragCount - 1); if (dragCount === 0) showDrop(false); });
        main.addEventListener('drop', (e) => {
            if (!hasFiles(e)) return;
            e.preventDefault();
            dragCount = 0;
            showDrop(false);
            addFiles([...e.dataTransfer.files]);
        });

        // Mobile Sidebar Toggle
        document.getElementById('btn-toggle-menu').addEventListener('click', () => {
            document.getElementById('ai-history').classList.toggle('open');
        });
    }

    function showDrop(on) {
        dropVeil.hidden = !on;
        desk.classList.toggle('is-drag', on);
    }

    function autoGrow() {
        inputBox.style.height = 'auto';
        inputBox.style.height = `${Math.min(inputBox.scrollHeight, 190)}px`;
    }

    function refreshSendButton() {
        const length = inputBox.value.length;
        qCount.textContent = `${length}/${MAX_QUESTION}`;
        qCount.classList.toggle('is-over', length >= MAX_QUESTION);

        const canAttach = isReady && !askController;
        btnPickImage.disabled = btnPickFile.disabled = !canAttach;
        btnSend.disabled = !isReady || !!askController || (!inputBox.value.trim() && pendingFileList.length === 0);
    }

    // ==================== TỆP ĐÍNH KÈM ====================
    function extOf(name) { return (name.split('.').pop() || '').toLowerCase(); }
    function isImageFile(file) { return IMAGE_EXT.includes(extOf(file.name)); }

    function sizeText(byte) {
        if (byte >= 1024 * 1024) return `${(byte / 1024 / 1024).toFixed(1)} MB`;
        return `${Math.max(1, Math.round(byte / 1024))} KB`;
    }

    function addFiles(fileList) {
        if (!isReady || askController) return;

        for (const file of fileList) {
            if (pendingFileList.length >= MAX_FILES) { toastErr(`Mỗi câu hỏi chỉ đính kèm tối đa ${MAX_FILES} tệp.`); break; }

            const ext = extOf(file.name) || (file.type === 'image/png' ? 'png' : '');
            if (!IMAGE_EXT.includes(ext) && !DOC_EXT.includes(ext)) {
                toastErr(`Chưa hỗ trợ tệp "${file.name}". Hãy gửi ảnh PNG, JPG, WEBP hoặc tệp PDF, DOCX, TXT.`);
                continue;
            }
            if (file.size > MAX_FILE_BYTE) { toastErr(`Tệp "${file.name}" lớn hơn ${MAX_FILE_BYTE / 1024 / 1024} MB.`); continue; }
            if (pendingFileList.some(x => x.file.name === file.name && x.file.size === file.size)) continue;

            // Ảnh dán từ clipboard thường tên "image.png" — giữ nguyên, trùng tên thì khác dung lượng vẫn qua được.
            pendingFileList.push({ file, url: IMAGE_EXT.includes(ext) ? URL.createObjectURL(file) : null });
        }

        renderTray();
        refreshSendButton();
        inputBox.focus();
    }

    function removeFile(index) {
        const [removed] = pendingFileList.splice(index, 1);
        if (removed?.url) URL.revokeObjectURL(removed.url);
        renderTray();
        refreshSendButton();
    }

    function clearFiles() {
        pendingFileList.forEach(x => x.url && URL.revokeObjectURL(x.url));
        pendingFileList = [];
        renderTray();
    }

    function renderTray() {
        tray.hidden = pendingFileList.length === 0;
        tray.innerHTML = pendingFileList.map((x, i) => `
            <div class="tile">
                <div class="tile-thumb">${x.url ? `<img src="${x.url}" alt="">` : esc(extOf(x.file.name))}</div>
                <div class="tile-meta"><b title="${esc(x.file.name)}">${esc(x.file.name)}</b><span>${sizeText(x.file.size)}</span></div>
                <button type="button" class="tile-x" data-remove="${i}" aria-label="Bỏ tệp"><i class="ph ph-x"></i></button>
            </div>`).join('');
        tray.querySelectorAll('[data-remove]').forEach(el => el.addEventListener('click', () => removeFile(Number(el.dataset.remove))));
    }

    // ==================== TRẠNG THÁI AI ====================
    async function loadStatus() {
        const card = document.getElementById('status-card');
        try {
            const status = await API.get('/aitutor/status');
            isReady = !!status.isReady;
            card.hidden = isReady;
            if (!isReady) card.innerHTML = `<i class="ph ph-warning-circle"></i> ${esc(status.message)}`;
        } catch (ex) {
            isReady = false;
            card.hidden = false;
            card.innerHTML = `<i class="ph ph-warning"></i> ${esc(ex.message)}`;
        }
        inputBox.disabled = !isReady;
        refreshSendButton();
    }

    // ==================== LỊCH SỬ ====================
    async function loadSessionList() {
        try { sessionList = await API.get('/aitutor/session'); } catch { sessionList = []; }
        renderHistory();
    }

    function renderHistory() {
        btnClearAll.hidden = sessionList.length === 0;

        if (sessionList.length === 0) {
            historyBox.innerHTML = `<div style="text-align:center; padding: 40px 10px; color: var(--ai-text-muted); font-size: 13px;">Chưa có hội thoại nào.</div>`;
            return;
        }

        historyBox.innerHTML = `<div class="ai-history-group">Gần đây</div>` + sessionList.map(x => `
            <div class="history-item ${x.id === sessionId ? 'active' : ''}" data-open="${x.id}">
                <div class="h-title" title="${esc(x.title)}">${esc(x.title)}</div>
                <div class="h-act">
                    <button type="button" data-rename="${x.id}"><i class="ph ph-pencil-simple"></i></button>
                    <button type="button" data-del="${x.id}"><i class="ph ph-trash"></i></button>
                </div>
            </div>`).join('');

        historyBox.querySelectorAll('[data-open]').forEach(el => el.addEventListener('click', (e) => {
            if (e.target.closest('[data-rename], [data-del]')) return;
            openSession(Number(el.dataset.open));
        }));
        historyBox.querySelectorAll('[data-rename]').forEach(el => el.addEventListener('click', () => renameSession(Number(el.dataset.rename))));
        historyBox.querySelectorAll('[data-del]').forEach(el => el.addEventListener('click', () => deleteSession(Number(el.dataset.del))));
    }

    async function openSession(id) {
        if (askController) { toastErr('Đang xử lý. Vui lòng đợi.'); return; }

        // Đóng sidebar trên mobile nếu đang mở
        document.getElementById('ai-history').classList.remove('open');

        logBox.innerHTML = `<div style="margin:auto; color:var(--ai-text-muted)"><span class="spinner-inline"></span> Đang tải...</div>`;
        try {
            const detail = await API.get(`/aitutor/session/${id}`);
            sessionId = detail.id;
            localStorage.setItem(LAST_SESSION_KEY, String(detail.id));
            logBox.innerHTML = '';
            turnCount = 0;

            for (const message of detail.messageList) {
                if (message.role === 'user') {
                    // Tin có tệp: tách dòng "[Đính kèm: ...]" ra khỏi nội dung. Tệp máy chủ còn giữ (attachmentList)
                    // thành thẻ bấm để tải; câu hỏi gửi trước khi hệ thống giữ tệp thì chỉ còn tên trong dòng đó.
                    const match = message.content.match(ATTACH_MARKER);
                    const fileList = message.attachmentList?.length
                        ? message.attachmentList.map(a => ({ name: a.fileName, attachmentId: a.id }))
                        : (match ? match[1].split(';').map(x => x.trim()).filter(Boolean).map(name => ({ name })) : []);
                    appendMessage('me', answerHtml(match ? message.content.slice(match[0].length) : message.content),
                        fileList, false, message.createdAt);
                } else {
                    appendMessage('ai', answerHtml(message.content), [], true, message.createdAt);
                }
            }
            if (turnCount === 0) renderWelcome();
            renderHistory();
            scrollToEnd();
        } catch (ex) {
            toastErr(ex.message);
            await newConversation();
            await loadSessionList();
        }
    }

    async function newConversation() {
        if (askController) return;
        document.getElementById('ai-history').classList.remove('open');
        sessionId = null;
        localStorage.removeItem(LAST_SESSION_KEY);
        turnCount = 0;
        timerBox.textContent = '';
        clearFiles();
        renderWelcome();
        renderHistory();
        refreshSendButton();
        inputBox.focus();
    }

    async function renameSession(id) {
        const current = sessionList.find(x => x.id === id);
        const title = await promptDialog('Đổi tên hội thoại', { value: current?.title ?? '', okText: 'Lưu' });
        if (title === null) return;
        try { await API.put(`/aitutor/session/${id}`, { title }); await loadSessionList(); }
        catch (ex) { toastErr(ex.message); }
    }

    async function deleteSession(id) {
        if (!await confirmDialog('Bạn có chắc chắn muốn xóa hội thoại này?', { danger: true, okText: 'Xóa' })) return;
        try {
            await API.del(`/aitutor/session/${id}`);
            if (id === sessionId) await newConversation();
            await loadSessionList();
        } catch (ex) { toastErr(ex.message); }
    }

    async function deleteAllSession() {
        if (!await confirmDialog('Xóa toàn bộ lịch sử hỏi đáp?', { danger: true, okText: 'Xóa tất cả' })) return;
        try {
            await API.del('/aitutor/session');
            await newConversation();
            await loadSessionList();
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== RENDER ====================
    function renderWelcome() {
        const suggestList = [
            { icon: 'pencil-line', text: 'Tóm tắt nội dung chính khóa học' },
            { icon: 'gear-six', text: 'Giải thích quy trình nghiệp vụ' },
            { icon: 'compass', text: 'Quy định giờ làm việc công ty' },
            { icon: 'lightbulb', text: 'Quy tắc an toàn lao động' }
        ];

        logBox.innerHTML = `
            <div class="ai-welcome">
                <div class="hello-mark pico-face" aria-hidden="true"></div>
                <h1>${esc(Hi())}, ${esc(Auth.profile.fullName)}</h1>
                <p>Hỏi về tài liệu và bài học nội bộ — hoặc gửi một ảnh, tệp PDF / Word để mình đọc và phân tích giúp bạn.</p>
                <div class="ai-suggest-grid">
                    ${suggestList.map(x => `
                        <div class="ai-suggest-card" data-text="${esc(x.text)}">
                            <i class="ph ph-${x.icon}"></i>
                            <span>${esc(x.text)}</span>
                        </div>
                    `).join('')}
                    <div class="ai-suggest-card is-attach" data-attach="1">
                        <i class="ph ph-paperclip"></i>
                        <span>Gửi ảnh hoặc tệp để HB Minerva phân tích</span>
                    </div>
                </div>
            </div>`;

        logBox.querySelectorAll('.ai-suggest-card[data-text]').forEach(btn => btn.addEventListener('click', () => {
            inputBox.value = btn.dataset.text;
            autoGrow();
            refreshSendButton();
            ask();
        }));
        logBox.querySelector('[data-attach]')?.addEventListener('click', () => inFile.click());
    }

    function Hi() {
        const hour = new Date().getHours();
        if (hour >= 6 && hour < 12) return t('greeting.morning', 'Chào buổi sáng');
        if (hour >= 12 && hour < 14) return t('greeting.noon', 'Chào buổi trưa');
        if (hour >= 14 && hour < 18) return t('greeting.afternoon', 'Chào buổi chiều');
        if (hour >= 18 && hour < 24) return t('greeting.evening', 'Chào buổi tối');
        return t('greeting.night', 'Chúc bạn ngủ ngon');
    }

    /** Giờ gửi của một tin: hôm nay chỉ hiện "14:05", ngày khác thì thêm ngày/tháng. */
    function timeText(value) {
        const date = value ? new Date(value) : new Date();
        const hm = date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
        return date.toDateString() === new Date().toDateString()
            ? hm
            : `${hm} · ${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}`;
    }

    /** Thêm một tin vào cuộc trò chuyện: side 'me' = tin của người dùng (bên phải; fileList là các tệp đã gửi kèm,
     *  dạng { name, attachmentId?, blob? } — có attachmentId hoặc blob thì thẻ tệp bấm để tải về), side 'ai' = tin của HB Minerva (bên trái; withActions = hiện ngay nút Sao chép, không thì chỉ
     *  hiện sau khi trả lời xong). createdAt là giờ lưu trong lịch sử, bỏ trống = bây giờ.
     *  Trả về phần bong bóng để nơi gọi ghi nội dung vào. */
    function appendMessage(side, html, fileList = [], withActions = false, createdAt = null) {
        if (turnCount === 0) logBox.innerHTML = '';
        turnCount++;

        const row = document.createElement('div');
        row.className = `msg ${side}`;

        if (side === 'me') {
            row.innerHTML = `
                <div class="msg-col">
                    ${fileList.length ? `<div class="q-files">${fileList.map((x, i) => {
                        const inner = `<b>${esc(extOf(x.name) || 'tệp')}</b><span>${esc(x.name)}</span>`;
                        return x.attachmentId || x.blob
                            ? `<button type="button" class="q-file" data-file="${i}" title="Tải xuống ${esc(x.name)}">${inner}<i class="ph ph-download-simple"></i></button>`
                            : `<span class="q-file" title="${esc(x.name)} — tệp gửi trước đây không được lưu lại">${inner}</span>`;
                    }).join('')}</div>` : ''}
                    <div class="msg-bubble"></div>
                    <span class="msg-time">${timeText(createdAt)}</span>
                </div>`;
            row.querySelectorAll('[data-file]').forEach(el => el.addEventListener('click', () => downloadFile(fileList[Number(el.dataset.file)])));
        } else {
            row.innerHTML = `
                <div class="msg-avatar pico-face" aria-hidden="true"></div>
                <div class="msg-col">
                    <div class="a-name">HB Minerva <span class="a-state"></span></div>
                    <div class="msg-bubble"></div>
                    <div class="a-foot" ${withActions ? '' : 'hidden'}>
                        <span class="msg-time" style="margin:0">${timeText(createdAt)}</span>
                        <button type="button" data-copy><i class="ph ph-copy"></i> Sao chép</button>
                    </div>
                </div>`;
            row.querySelector('[data-copy]').addEventListener('click', async (e) => {
                const button = e.currentTarget;
                try {
                    await navigator.clipboard.writeText(row.querySelector('.msg-bubble').innerText);
                    button.innerHTML = '<i class="ph ph-check"></i> Đã chép';
                    setTimeout(() => { button.innerHTML = '<i class="ph ph-copy"></i> Sao chép'; }, 1500);
                } catch { toastErr('Trình duyệt không cho phép sao chép.'); }
            });
        }

        const body = row.querySelector('.msg-bubble');
        body.innerHTML = html;
        logBox.appendChild(row);
        scrollToEnd();
        return body;
    }

    /** Tải lại một tệp đã gửi. Tin vừa gửi trong phiên này còn giữ chính tệp trong trình duyệt (blob) nên lưu thẳng;
     *  tin mở lại từ lịch sử thì tải qua API — API.download gửi kèm token, thẻ <a href> thường thì không. */
    async function downloadFile(item) {
        if (item.blob) {
            const url = URL.createObjectURL(item.blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = item.name;
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            return;
        }
        try {
            await API.download(`/aitutor/attachment/${item.attachmentId}`, item.name);
        } catch (ex) {
            toastErr(ex.status === 404 ? 'Tệp này không còn trên máy chủ.' : 'Không tải được tệp. Vui lòng thử lại.');
        }
    }

    function scrollToEnd() {
        // Cuộn khung bao ngoài thay vì logBox
        scrollArea.scrollTop = scrollArea.scrollHeight;
    }

    /** Chữ thô của model -> HTML an toàn.
     *  XÓA luôn các số trích dẫn [1] [2] thay vì tô chúng lên: người học không còn xem bảng nguồn nữa, nên
     *  mấy con số đó chỉ là những cái móc trỏ vào hư không — đọc tới là khựng lại tự hỏi nó nghĩa là gì.
     *  Cắt ở ĐÂY chứ không chỉ sửa câu lệnh cho model, để những câu trả lời ĐÃ LƯU trong lịch sử (vốn còn
     *  đầy [1] [2]) mở lại cũng sạch.
     *  Phần định dạng (bảng, **đậm**, `mã`, gạch đầu dòng) do aiAnswerHtml trong app.js lo. Escape TRƯỚC rồi mới
     *  thay, nên chữ model sinh ra không bao giờ chèn được thẻ HTML. */
    function answerHtml(text) {
        return aiAnswerHtml(esc(text).replace(/ ?\[\d{1,2}\]/g, ""));
    }

    // ==================== HỎI ĐÁP ====================
    async function ask() {
        const question = inputBox.value.trim();
        const sendFileList = pendingFileList.map(x => x.file);
        if (!question && sendFileList.length === 0) return;
        if (askController) return;

        appendMessage('me', question ? answerHtml(question) : '<em>Phân tích tệp đính kèm</em>', sendFileList.map(f => ({ name: f.name, blob: f })));
        inputBox.value = '';
        autoGrow();
        clearFiles();

        const bubble = appendMessage('ai', '<div class="typing-indicator"><i></i><i></i><i></i></div>');
        const card = bubble.closest('.msg-col');
        const stateBox = card.querySelector('.a-state');
        stateBox.textContent = sendFileList.length ? 'Đang đọc tệp đính kèm — có thể mất 1–2 phút' : 'Đang tra tài liệu...';

        askController = new AbortController();
        btnStop.hidden = false;
        btnSend.hidden = true;
        desk.classList.add('is-busy');
        refreshSendButton();

        const startedAt = Date.now();
        const timer = setInterval(() => { timerBox.textContent = `${Math.round((Date.now() - startedAt) / 1000)}s`; }, 1000);

        let answer = '';

        function renderAnswer(pending) {
            bubble.innerHTML = answerHtml(answer) + (pending ? '<span class="ai-cursor"></span>' : '');
            if (scrollArea.scrollHeight - scrollArea.scrollTop - scrollArea.clientHeight < 200) scrollToEnd();
        }

        try {
            // multipart/form-data (không tự đặt Content-Type: trình duyệt phải tự thêm boundary)
            const form = new FormData();
            form.append('question', question);
            if (sessionId) form.append('sessionId', String(sessionId));
            sendFileList.forEach(file => form.append('files', file, file.name));

            const response = await fetch('/api/aitutor/ask', {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${Auth.token}` },
                body: form,
                signal: askController.signal
            });

            if (response.status === 401) { Auth.clear(); location.href = '/pages/login/login.html'; return; }
            if (!response.ok) throw new Error(`Lỗi máy chủ: ${response.status}`);

            const reader = response.body.getReader();
            const decoder = new TextDecoder('utf-8');
            let buffer = '';

            while (true) {
                const { value, done } = await reader.read();
                if (done) break;

                buffer += decoder.decode(value, { stream: true });
                let cut;
                while ((cut = buffer.indexOf('\n\n')) >= 0) {
                    const raw = buffer.slice(0, cut);
                    buffer = buffer.slice(cut + 2);

                    const name = raw.match(/^event: (.+)$/m)?.[1];
                    const dataText = raw.match(/^data: ([\s\S]*)$/m)?.[1];
                    if (!name || !dataText) continue;

                    let data;
                    try { data = JSON.parse(dataText); } catch { continue; }

                    // Sự kiện "sources" của máy chủ bị bỏ qua có chủ đích: người học chỉ xem câu trả lời,
                    // không xem danh sách tài liệu đã tra.
                    if (name === 'token') { answer += data.t; stateBox.textContent = 'Đang soạn câu trả lời...'; renderAnswer(true); }
                    else if (name === 'session') {
                        sessionId = data.id;
                        localStorage.setItem(LAST_SESSION_KEY, String(data.id));
                    }
                    else if (name === 'error') throw new Error(data.message);
                }
            }

            renderAnswer(false);
            if (!answer.trim()) bubble.innerHTML = `Hệ thống chưa tìm thấy thông tin phù hợp trong tài liệu.`;

        } catch (ex) {
            if (ex.name === 'AbortError') {
                renderAnswer(false);
            } else {
                bubble.innerHTML = `<span style="color:var(--danger);"><i class="ph ph-warning"></i> ${esc(ex.message)}</span>`;
                toastErr(ex.message);
            }
        } finally {
            clearInterval(timer);
            timerBox.textContent = '';
            stateBox.textContent = '';
            if (answer.trim()) card.querySelector('.a-foot').hidden = false;
            askController = null;
            btnStop.hidden = true;
            btnSend.hidden = false;
            desk.classList.remove('is-busy');
            refreshSendButton();
            inputBox.focus();
            await loadSessionList();
        }
    }
})();
