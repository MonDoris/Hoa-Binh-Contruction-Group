// File: wwwroot/pages/online-class/classroom.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    if (!Auth.require(['Trưởng Nhóm', 'Giảng Viên', 'Học viên', 'Trưởng phòng', 'Giám Đốc', 'Tổng Giám Đốc'], 'online-class.view')) return;
    ensurePhosphorIcons();
    ensureUicons();

    // Mã tham gia (từ liên kết mời, hoặc tự gõ ở trang danh sách) — bắt buộc với người không phải chủ trì/Admin,
    // xem ClassRoomHub.JoinRoom. Hỗ trợ cả liên kết chỉ có "code" (không kèm sẵn id) — tra id trước qua API.
    const joinCode = (qs('code') || '').trim();
    let roomId = parseInt(qs('id') || '0', 10);
    if (!roomId && joinCode) {
        try { roomId = (await API.get(`/onlineclassroom/resolve-code/${encodeURIComponent(joinCode)}`)).id; }
        catch (ex) { document.getElementById('cr-placeholder').textContent = ex.message; return; }
    }
    if (!roomId) { location.href = '/pages/online-class/online-class.html'; return; }

    // Lấy cấu hình STUN/TURN từ server (server tự triển khai — không qua bên thứ ba nào). Nếu
    // backend chưa cấu hình mục "Turn" (appsettings), API trả về rỗng và WebRTC chỉ dùng được
    // trong cùng mạng LAN/VPN công ty (chỉ có ứng viên host nội bộ, không xuyên NAT được).
    let ICE_SERVERS = [];
    try { ICE_SERVERS = await API.get('/onlineclassroom/ice-servers'); } catch { ICE_SERVERS = []; }

    let room = null;
    let isHost = false;
    let myConnectionId = null;
    let localStream = null;      // host: camera/mic (hoặc màn hình đang chia sẻ); viewer: mic (+ camera nếu tự bật)
    let cameraTrack = null;      // track camera gốc của chính mình (host: có sẵn; viewer: xin quyền khi bấm nút cam), dùng để phục hồi sau khi tắt chia sẻ màn hình
    let myCamSender = null;      // viewer: RTCRtpSender của kênh video gửi tới host, dùng để gắn camera vào khi bật (không cần đàm phán lại)
    const peerList = {};         // connectionId -> { pc, connectionId, fullName, isHost, micOn, camOn, videoTrack }
    let mediaRecorder = null;    // host: ghi lại toàn bộ giao diện phòng học (quay tab) + mic để lưu thành bản ghi buổi học
    let displayStream = null;    // luồng hình quay nguyên tab trình duyệt (xin quyền qua getDisplayMedia, gồm cả giao diện chat/người tham gia/đồng hồ)
    let recordingId = null;      // id bản ghi phía server (tạo lúc StartRecording), dùng để gửi từng đoạn video nhỏ lên
    let recordingStartAt = null; // mốc thời gian bắt đầu ghi (đồng hồ trình duyệt), dùng tính thời lượng thật lúc kết thúc + chạy đồng hồ hiển thị
    let recTimerInterval = null;
    let uploadQueue = Promise.resolve(); // hàng đợi tải từng đoạn lên tuần tự, tránh 2 đoạn ghi đè lẫn nhau trên server
    // Trạng thái ghi hình do SERVER giữ và phát cho cả phòng (xem ClassRoomHub: RecordingState). Mỗi trình duyệt chỉ
    // chạy đồng hồ hiển thị cục bộ từ con số server gửi, nên người vào muộn thấy đúng thời gian và không ai lệch nhau.
    let recState = 'Idle';       // 'Idle' | 'Recording' | 'Paused'
    let recBaseMs = 0;           // thời gian đã ghi (ms, không tính đoạn tạm dừng) tại lúc nhận trạng thái
    let recBaseAt = 0;           // performance.now() lúc nhận trạng thái
    let recActiveMs = 0;         // host: thời gian đã ghi THẬT của Bản ghi hiện tại, tự cộng dồn để vá thời lượng tệp
    let recResumedAt = 0;        // host: Date.now() lúc bắt đầu / tiếp tục đoạn đang chạy
    let meetingTimer = null;     // đồng hồ "đã diễn ra" của buổi học trên thanh trên
    let unreadCount = 0;         // tin nhắn chat chưa đọc khi khung chat đang đóng
    // Lưới người tham gia (xem renderGallery). Khai báo ở ĐÂY, không để cạnh code dùng chúng: sự kiện của hub chạy được ngay
    // khi hàm khởi động còn đang chờ ở `await`, nên một `const/let` nằm phía dưới sẽ báo lỗi "Cannot access before initialization".
    const GAL_GAP = 8, GAL_PAD = 8, GAL_MAX_TILES = 16, GAL_MIN_W = 150, GAL_MIN_H = 96;
    const GAL_MIN_ASPECT = 0.75, GAL_MAX_ASPECT = 16 / 9, GAL_MORE_KEY = '__more';
    const tileMap = new Map();   // khoá ('self' hoặc connectionId) -> phần tử ô
    let joined = false;          // đã vào phòng xong (nhận Joined); trước đó khung chỉ có dòng "Đang kết nối..."
    let hostStream = null;       // học viên: luồng hình + tiếng nhận từ Chủ trì
    let mySharing = false;       // mình đang chia sẻ màn hình
    let myScreenTrack = null;    // học viên: track màn hình đang chia sẻ (gửi tới Chủ trì)
    let pinnedKey = null;        // ô người dùng ghim
    let dismissedKey = null;     // người chia sẻ màn hình mà người dùng đã thu về lưới
    let currentSpotKey = null;   // ô đang phóng to ở lần vẽ gần nhất
    let galleryFrame = 0;

    try {
        room = await API.get(`/onlineclassroom/${roomId}`);
    } catch (ex) {
        document.getElementById('cr-placeholder').textContent = ex.message;
        return;
    }
    isHost = room.hostUserId === Auth.profile?.id;
    document.getElementById('cr-title').textContent = room.title;
    document.getElementById('cr-status').innerHTML = statusBadge(room.status);

    // room.joinCode chỉ khác null với chủ trì/Admin (API tự ẩn với người khác) — hiện ngay trong phòng để
    // chủ trì tiện đọc/gửi mã cho người tham gia mà không phải quay lại trang danh sách.
    if (room.joinCode) {
        const codeBadge = document.getElementById('cr-code-badge');
        codeBadge.classList.add('show');
        // Mỗi ký tự 1 ô riêng — cùng kiểu với các ô OTP lúc nhập mã ở trang danh sách, để người chủ trì
        // nhận ra ngay đây là mã sẽ gõ vào từng ô đó (mã gồm cả chữ lẫn số, không phải chỉ toàn số).
        const charsHtml = [...room.joinCode].map(ch => `<span class="cr-code-char">${esc(ch)}</span>`).join('');
        codeBadge.innerHTML = `
            <span data-i18n="classroom.maPhong" class="cr-code-label">Mã phòng</span>
            <span class="cr-code-chars">${charsHtml}</span>
            <button type="button" id="cr-copy-code" class="cr-copy-btn" data-i18n-title="classroom.saoChepLienKetMoi" title="Sao chép liên kết mời"><i class="ph ph-copy"></i></button>`;
        const copyBtn = document.getElementById('cr-copy-code');
        copyBtn.addEventListener('click', async () => {
            const link = `${location.origin}/pages/online-class/classroom.html?id=${roomId}&code=${encodeURIComponent(room.joinCode)}`;
            try {
                await navigator.clipboard.writeText(link);
                toastOk(t('classroom.daSaoChepLienKetMoiVaoPhong', 'Đã sao chép liên kết mời vào phòng.'));
                copyBtn.classList.add('copied');
                copyBtn.innerHTML = '<i class="ph ph-check"></i>';
                setTimeout(() => { copyBtn.classList.remove('copied'); copyBtn.innerHTML = '<i class="ph ph-copy"></i>'; }, 1600);
            } catch { toastErr(t('classroom.khongSaoChepDuocTrinhDuyetChan', 'Không sao chép được — trình duyệt chặn quyền truy cập clipboard.')); }
        });
    }

    if (room.status === 'Ended' || room.status === 'Cancelled') {
        document.getElementById('cr-placeholder').textContent = t('classroom.buoiHocDaKetThucHoacBiHuy', 'Buổi học đã kết thúc hoặc bị hủy.');
        return;
    }

    // Màn hình xem trước: kiểm tra thiết bị, chờ buổi học bắt đầu (học viên). Chỉ khi bấm vào mới mở kết nối bên dưới.
    const joinPrefs = await runPreview();

    renderControls();

    // Vẽ lại lưới mỗi khi khung đổi cỡ (xoay màn hình, đóng/mở khung chat...). Ảnh đại diện lỗi thì gỡ ảnh, còn chữ cái đầu.
    document.getElementById('cr-gallery').setAttribute('aria-label', t('classroom.glGallery', 'Người tham gia trong phòng'));
    if (window.ResizeObserver) new ResizeObserver(scheduleGallery).observe(document.getElementById('cr-gallery'));
    else window.addEventListener('resize', scheduleGallery);
    document.getElementById('cr-people-list').addEventListener('error', e => { if (e.target.tagName === 'IMG') e.target.remove(); }, true);

    document.querySelectorAll('.cr-side-tabs button[data-panel]').forEach(b => b.addEventListener('click', () => {
        document.querySelectorAll('.cr-side-tabs button[data-panel]').forEach(x => x.classList.remove('active'));
        document.querySelectorAll('.cr-side-panel').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        document.getElementById(`panel-${b.dataset.panel}`).classList.add('active');
    }));
    document.getElementById('cr-chat-send').addEventListener('click', sendChat);
    document.getElementById('cr-chat-text').addEventListener('keydown', e => { if (e.key === 'Enter') sendChat(); });

    // Đóng/mở khung chat. Màn hình rộng: thu hẹp .cr-side về 0 để nhường chỗ cho video. Màn hình nhỏ: khung là tấm trượt từ
    // dưới lên che bớt video nên MẶC ĐỊNH ĐÓNG. Nút ở thanh trên (có chấm đỏ báo tin chưa đọc) và nút "×" trong khung cùng đóng/mở.
    function setSidePanel(open) {
        document.getElementById('cr-side').classList.toggle('collapsed', !open);
        document.querySelector('.cr-body').classList.toggle('side-collapsed', !open);
        document.getElementById('cr-side-toggle').setAttribute('aria-expanded', String(open));
        if (open) clearUnread();
    }
    function toggleSidePanel() { setSidePanel(document.getElementById('cr-side').classList.contains('collapsed')); }
    function clearUnread() {
        unreadCount = 0;
        document.getElementById('cr-unread').hidden = true;
    }
    document.getElementById('cr-side-toggle').addEventListener('click', toggleSidePanel);
    document.getElementById('cr-side-close').addEventListener('click', toggleSidePanel);
    if (window.matchMedia('(max-width: 860px)').matches) setSidePanel(false);

    // ==================== KẾT NỐI SIGNALR ====================
    const connection = new signalR.HubConnectionBuilder()
        // Vé ngắn hạn thay cho thẻ đăng nhập — xem Auth.hubTicket().
        .withUrl('/hubs/classroom', { accessTokenFactory: () => Auth.hubTicket() })
        .withAutomaticReconnect()
        .build();

    connection.on('Error', msg => { document.getElementById('cr-placeholder').textContent = msg; toastErr(msg); });

    connection.on('Joined', async ({ self, otherList, recording }) => {
        myConnectionId = self.connectionId;
        // Ghi nhận MỌI người đã có mặt (không chỉ Chủ trì) để lưới và danh sách đầy đủ ngay từ lúc vào.
        otherList.forEach(upsertPeer);
        joined = true;
        renderPeopleList();
        if (recording) applyRecordingState(recording);

        adoptPreviewMedia(joinPrefs);
        if (isHost) {
            // Báo mic/camera ban đầu của Chủ trì: người vào sau mới biết lúc nào hiện ảnh đại diện thay cho khung đen.
            if (localStream.getAudioTracks()[0]?.enabled) connection.invoke('ToggleMic', roomId, true);
            if (localStream.getVideoTracks()[0]?.enabled) connection.invoke('ToggleCam', roomId, true);
        } else {
            const host = otherList.find(p => p.isHost);
            if (host) await connectToHost(host);
            // Học viên đã bật camera ở bước xem trước: gắn vào kênh gửi tới chủ trì (cùng cách khi bấm nút camera sau này)
            // và báo cho chủ trì thấy. Đưa vào luồng của phòng SAU khi kết nối, để connectToHost không thêm nó thành một kênh gửi thừa.
            if (cameraTrack && myCamSender) {
                localStream.addTrack(cameraTrack);
                await myCamSender.replaceTrack(cameraTrack);
                connection.invoke('ToggleCam', roomId, true);
            }
            if (joinPrefs.micOn && localStream.getAudioTracks()[0]) connection.invoke('ToggleMic', roomId, true);
        }
        syncMicButton();
        syncCamButton();
        document.getElementById('btn-share').disabled = false;
        if (!isHost) document.getElementById('btn-cam').disabled = false; // chủ trì: syncCamButton đã mở khóa nếu có camera
        if (isHost) document.getElementById('btn-record').disabled = false;
        scheduleGallery();

        // Lấy lại thông tin buổi học (giờ bắt đầu thật, trạng thái Đang diễn ra) rồi chạy đồng hồ trên thanh trên.
        startMeetingClock();
    });

    connection.on('ParticipantJoined', (p) => {
        upsertPeer(p);
        renderPeopleList();
        scheduleGallery();
    });

    connection.on('ParticipantLeft', (connectionId) => {
        const peer = peerList[connectionId];
        if (peer?.pc) peer.pc.close();
        delete peerList[connectionId];
        document.getElementById(`audio-${connectionId}`)?.remove();
        renderPeopleList();
        scheduleGallery();
    });

    connection.on('ParticipantMicToggled', (connectionId, isOn) => {
        if (peerList[connectionId]) peerList[connectionId].micOn = isOn;
        renderPeopleList();
        scheduleGallery();
    });

    connection.on('ParticipantCamToggled', (connectionId, isOn) => {
        if (peerList[connectionId]) peerList[connectionId].camOn = isOn;
        renderPeopleList();
        scheduleGallery();
    });

    connection.on('ParticipantScreenToggled', (connectionId, isOn) => {
        if (peerList[connectionId]) peerList[connectionId].sharing = isOn;
        if (isOn && dismissedKey === connectionId) dismissedKey = null; // lần chia sẻ mới thì phóng to lại
        scheduleGallery();
    });

    connection.on('Signal', async (fromConnectionId, type, payload) => {
        const data = JSON.parse(payload);
        if (type === 'offer') await handleOfferAsHost(fromConnectionId, data);
        else if (type === 'answer') await peerList[fromConnectionId]?.pc?.setRemoteDescription(data);
        else if (type === 'candidate') { try { await peerList[fromConnectionId]?.pc?.addIceCandidate(data); } catch { } }
    });

    connection.on('ChatMessage', (fullName, message, time) => appendChat(fullName, message, time));

    connection.on('RecordingState', applyRecordingState);

    connection.on('RoomEnded', () => {
        toastOk(t('classroom.buoiHocDaKetThuc', 'Buổi học đã kết thúc.'));
        cleanupAndLeave();
        setTimeout(() => location.href = '/pages/online-class/online-class.html', 1500);
    });

    connection.onreconnected(() => location.reload());
    try {
        await connection.start();
        await connection.invoke('JoinRoom', roomId, joinCode);
    } catch (ex) {
        document.getElementById('cr-placeholder').textContent = t('classroom.khongKetNoiDuocMayChuBaoHieu', 'Không kết nối được máy chủ báo hiệu.');
        toastErr(ex.message || t('classroom.loiKetNoi', 'Lỗi kết nối.'));
    }

    // ==================== MÀN HÌNH XEM TRƯỚC ====================
    // Bước mọi người đi qua TRƯỚC khi mở kết nối vào phòng: xem camera, đo mic, chọn thiết bị, bật/tắt. Buổi học chỉ chính
    // thức bắt đầu khi Chủ trì bấm nút vào (JoinRoom ở phía server mới chuyển buổi sang Đang diễn ra) — nên chủ trì chỉnh
    // thiết bị thoải mái mà học viên chưa thấy gì. Học viên đến sớm chờ ở đây và nút Tham gia sáng lên khi buổi bắt đầu.
    //
    // Hàm này chạy trong lúc cả hàm khởi động còn đang tạm dừng ở `await runPreview()`, nên chỉ được dùng những biến đã
    // khai báo TRƯỚC điểm đó (room, isHost, joinCode, roomId) — mọi trạng thái riêng của nó nằm trong hàm.
    //
    // Trả về lựa chọn của người dùng (luồng media, bật/tắt) để phòng dùng lại đúng luồng này — không xin quyền hai lần,
    // không giữ camera hai lần.
    async function runPreview() {
        const el = (id) => document.getElementById(id);
        const overlay = el('cr-preview');
        const placeholder = el('cr-placeholder');
        placeholder.hidden = true;
        overlay.hidden = false;
        // Phần phòng nằm phía sau màn hình xem trước (khung chat, thanh điều khiển, nút ở thanh trên) chưa dùng được:
        // khóa lại để phím Tab và trình đọc màn hình không "rò" vào đó. Mở lại ngay khi vào phòng.
        const behind = document.querySelectorAll('.cr-stage, .cr-side, .cr-controls, .cr-top-actions');
        behind.forEach(node => { node.inert = true; });

        const pv = {
            stream: null, videoTrack: null, audioTrack: null,
            // Chủ trì mặc định bật cả camera và mic; học viên mặc định tắt cả hai (như trước đây).
            camOn: isHost, micOn: isHost,
            cameraId: '', micId: '', camList: [], micList: [],
            status: room.status, codeError: '', needCode: false, noMedia: '',
            ac: null, raf: 0, pollTimer: 0,
        };

        el('pv-room').textContent = `${room.title} · ${room.hostUserName}`;
        el('pv-sub').textContent = isHost
            ? t('classroom.pvSubHost', 'Kiểm tra camera và micro trước khi bắt đầu buổi học.')
            : t('classroom.pvSubViewer', 'Kiểm tra camera và micro trước khi vào phòng.');
        el('pv-avatar').textContent = initial(Auth.profile?.fullName);
        el('pv-join-as').textContent = t('classroom.pvJoinAs', 'Bạn sẽ vào với tư cách {name}', { name: Auth.profile?.fullName || '' });

        // Liên kết mời có mã: xác minh mã ngay ở đây thay vì để người dùng bấm vào rồi mới thấy lỗi.
        if (!isHost && !Auth.isAdmin) {
            if (joinCode) {
                try {
                    const found = await API.getSilent(`/onlineclassroom/resolve-code/${encodeURIComponent(joinCode)}`);
                    if (found.id !== roomId) pv.codeError = t('classroom.pvCodeBad', 'Mã tham gia không đúng hoặc buổi học không còn hoạt động.');
                } catch { pv.codeError = t('classroom.pvCodeBad', 'Mã tham gia không đúng hoặc buổi học không còn hoạt động.'); }
            } else pv.needCode = true;
        }

        return new Promise((resolve) => {
            // ---------- Thiết bị ----------
            const stopStream = () => { pv.stream?.getTracks().forEach(track => track.stop()); pv.stream = pv.videoTrack = pv.audioTrack = null; };

            async function acquire() {
                stopStream();
                stopMeter();
                pv.noMedia = '';
                if (!navigator.mediaDevices?.getUserMedia) { pv.noMedia = 'all'; await afterAcquire(); return; }

                const withId = (id) => id ? { deviceId: { exact: id } } : true;
                // Thử đủ cả hai; thiếu camera hoặc mic thì lùi về chỉ có một trong hai; vẫn không được thì vào chế độ chỉ nghe.
                const attemptList = () => [
                    { video: withId(pv.cameraId), audio: withId(pv.micId) },
                    { audio: withId(pv.micId) },
                    { video: withId(pv.cameraId) },
                ];
                for (const constraints of attemptList()) {
                    try { pv.stream = await navigator.mediaDevices.getUserMedia(constraints); break; } catch { pv.stream = null; }
                }
                // Thiết bị đã chọn biến mất (rút camera USB...): bỏ lựa chọn cũ và thử lại một lần với thiết bị mặc định.
                if (!pv.stream && (pv.cameraId || pv.micId)) { pv.cameraId = pv.micId = ''; await acquire(); return; }

                pv.videoTrack = pv.stream?.getVideoTracks()[0] || null;
                pv.audioTrack = pv.stream?.getAudioTracks()[0] || null;
                if (pv.videoTrack) pv.videoTrack.enabled = pv.camOn;
                if (pv.audioTrack) pv.audioTrack.enabled = pv.micOn;
                pv.noMedia = !pv.stream ? 'all' : !pv.videoTrack ? 'camera' : !pv.audioTrack ? 'mic' : '';
                await afterAcquire();
            }

            async function afterAcquire() {
                await listDevices();
                const video = el('pv-video');
                video.srcObject = pv.videoTrack ? new MediaStream([pv.videoTrack]) : null;
                startMeter();
                render();
            }

            async function listDevices() {
                try {
                    const all = await navigator.mediaDevices.enumerateDevices();
                    pv.camList = all.filter(d => d.kind === 'videoinput');
                    pv.micList = all.filter(d => d.kind === 'audioinput');
                } catch { pv.camList = pv.micList = []; }
                pv.cameraId = pv.videoTrack?.getSettings().deviceId || pv.cameraId;
                pv.micId = pv.audioTrack?.getSettings().deviceId || pv.micId;
            }

            // ---------- Thanh đo mic ----------
            // Đo bằng AnalyserNode trên đúng luồng mic đang dùng: tắt mic thì track im lặng nên thanh đứng yên.
            function startMeter() {
                stopMeter();
                const bars = [...document.querySelectorAll('#pv-meter i')];
                const Ctx = window.AudioContext || window.webkitAudioContext;
                if (!pv.audioTrack || !Ctx) { bars.forEach(b => b.classList.remove('on')); return; }

                pv.ac = new Ctx();
                pv.ac.resume?.();
                // Trình duyệt chỉ cho AudioContext chạy sau một thao tác của người dùng: lần chạm / bấm phím đầu tiên trên màn hình
                // này sẽ đánh thức thanh đo (không có thì thanh đứng yên dù mic đang nói).
                const wake = () => pv.ac?.resume?.();
                overlay.addEventListener('pointerdown', wake, { once: true });
                overlay.addEventListener('keydown', wake, { once: true });
                const analyser = pv.ac.createAnalyser();
                analyser.fftSize = 512;
                pv.ac.createMediaStreamSource(new MediaStream([pv.audioTrack])).connect(analyser);
                const data = new Uint8Array(analyser.fftSize);
                const frame = () => {
                    analyser.getByteTimeDomainData(data);
                    let sum = 0;
                    for (const v of data) { const x = (v - 128) / 128; sum += x * x; }
                    const level = Math.min(1, Math.sqrt(sum / data.length) * 4);
                    const lit = Math.round(level * bars.length);
                    bars.forEach((bar, i) => bar.classList.toggle('on', i < lit));
                    el('pv-meter').setAttribute('aria-valuenow', String(Math.round(level * 100)));
                    pv.raf = requestAnimationFrame(frame);
                };
                frame();
            }

            function stopMeter() {
                cancelAnimationFrame(pv.raf);
                pv.ac?.close().catch(() => { });
                pv.ac = null;
            }

            // ---------- Vẽ trạng thái ----------
            function canJoin() {
                if (pv.codeError) return false;
                if (pv.status === 'Ended' || pv.status === 'Cancelled') return false;
                // Chủ trì vào được cả khi buổi còn "Chờ" (vào là bắt đầu); người khác chỉ vào khi buổi đã diễn ra.
                return isHost || pv.status === 'Live';
            }

            function render() {
                const camUsable = !!pv.videoTrack;
                const micUsable = !!pv.audioTrack;

                // Camera
                const showVideo = camUsable && pv.camOn;
                el('pv-video').hidden = !showVideo;
                el('pv-off').hidden = showVideo;
                el('pv-off-text').textContent = camUsable
                    ? t('classroom.pvCameraOff', 'Camera đang tắt')
                    : t('classroom.pvNoCamera', 'Không tìm thấy camera hoặc chưa được cho phép');

                const camBtn = el('pv-toggle-cam'), micBtn = el('pv-toggle-mic');
                camBtn.disabled = !camUsable;
                micBtn.disabled = !micUsable;
                camBtn.setAttribute('aria-pressed', String(pv.camOn && camUsable));
                micBtn.setAttribute('aria-pressed', String(pv.micOn && micUsable));
                camBtn.classList.toggle('off', !(pv.camOn && camUsable));
                micBtn.classList.toggle('off', !(pv.micOn && micUsable));
                camBtn.querySelector('i').className = `ph ph-video-camera${pv.camOn && camUsable ? '' : '-slash'}`;
                micBtn.querySelector('i').className = `ph ph-microphone${pv.micOn && micUsable ? '' : '-slash'}`;
                const camLabel = pv.camOn ? t('classroom.pvCamOff', 'Tắt camera') : t('classroom.pvCamOn', 'Bật camera');
                const micLabel = pv.micOn ? t('classroom.pvMicOff', 'Tắt micro') : t('classroom.pvMicOn', 'Bật micro');
                camBtn.title = camLabel; camBtn.setAttribute('aria-label', camLabel);
                micBtn.title = micLabel; micBtn.setAttribute('aria-label', micLabel);

                // Ô chọn thiết bị: chỉ hiện ô chọn khi máy có từ hai thiết bị trở lên; một thiết bị thì chỉ ghi tên.
                fillDevices('pv-cam', pv.camList, pv.cameraId, t('classroom.pvNoCameraDevice', 'Không có camera'));
                fillDevices('pv-mic', pv.micList, pv.micId, t('classroom.pvNoMicDevice', 'Không có micro'));

                // Ghi chú trạng thái (đọc được bằng trình đọc màn hình)
                const notes = [];
                if (pv.codeError) notes.push(['bad', pv.codeError]);
                else if (pv.needCode) notes.push(['info', t('classroom.pvNeedCode', 'Bạn cần mã tham gia (hoặc liên kết mời) do chủ trì cung cấp.')]);
                if (pv.noMedia === 'all') notes.push(['warn', t('classroom.pvNoDevice', 'Chưa dùng được camera và micro (bị từ chối quyền hoặc không có thiết bị). Bạn vẫn có thể vào phòng để nghe.')]);
                else if (pv.noMedia === 'camera') notes.push(['warn', t('classroom.pvNoCameraOnly', 'Không dùng được camera — bạn vẫn có thể vào phòng.')]);
                else if (pv.noMedia === 'mic') notes.push(['warn', t('classroom.pvNoMicOnly', 'Không dùng được micro — bạn vẫn có thể vào phòng để xem.')]);
                el('pv-notes').innerHTML = notes.map(([kind, text]) => `<div class="pv-note ${kind}">${esc(text)}</div>`).join('');

                // Trạng thái buổi học + nút vào
                const ended = pv.status === 'Ended' || pv.status === 'Cancelled';
                const live = pv.status === 'Live';
                el('pv-status').className = `pv-status ${ended ? 'bad' : live ? 'live' : 'wait'}`;
                el('pv-status-text').textContent = ended ? t('classroom.pvEnded', 'Buổi học đã kết thúc hoặc bị hủy.')
                    : isHost ? (live ? t('classroom.pvLive', 'Buổi học đang diễn ra.') : t('classroom.pvHostStarts', 'Buổi học sẽ chính thức bắt đầu khi bạn vào phòng.'))
                    : live ? t('classroom.pvLive', 'Buổi học đang diễn ra.') : t('classroom.pvWaiting', 'Chủ trì chưa bắt đầu buổi học. Trang sẽ tự kiểm tra lại.');
                el('pv-status').querySelector('i').className = `ph-fill ph-${ended ? 'x-circle' : live ? 'broadcast' : 'hourglass-medium'}`;

                const join = el('pv-join');
                join.disabled = !canJoin();
                join.querySelector('span').textContent = isHost && !live ? t('classroom.pvStart', 'Bắt đầu buổi học')
                    : isHost || live ? t('classroom.pvEnter', 'Vào phòng') : t('classroom.pvJoin', 'Tham gia');
            }

            function fillDevices(selectId, list, currentId, emptyText) {
                const select = el(selectId);
                const nameBox = el(`${selectId}-name`);
                select.hidden = list.length < 2;
                nameBox.hidden = list.length >= 2;
                nameBox.textContent = list[0]?.label || emptyText;
                select.innerHTML = list.map((d, i) => `<option value="${esc(d.deviceId)}"${d.deviceId === currentId ? ' selected' : ''}>${esc(d.label || `${selectId === 'pv-cam' ? 'Camera' : 'Micro'} ${i + 1}`)}</option>`).join('');
            }

            // ---------- Sự kiện ----------
            el('pv-toggle-cam').onclick = () => {
                if (!pv.videoTrack) return;
                pv.camOn = !pv.camOn;
                pv.videoTrack.enabled = pv.camOn;
                render();
            };
            el('pv-toggle-mic').onclick = () => {
                if (!pv.audioTrack) return;
                pv.micOn = !pv.micOn;
                pv.audioTrack.enabled = pv.micOn;
                render();
            };
            el('pv-cam').onchange = async (e) => { pv.cameraId = e.target.value; await acquire(); };
            el('pv-mic').onchange = async (e) => { pv.micId = e.target.value; await acquire(); };
            navigator.mediaDevices?.addEventListener?.('devicechange', async () => { await listDevices(); render(); });

            // Buổi học chưa bắt đầu: kiểm tra lại vài giây một lần; đổi sang Đang diễn ra thì nút Tham gia sáng lên.
            if (pv.status === 'Scheduled' || !isHost) {
                pv.pollTimer = setInterval(async () => {
                    try {
                        const latest = await API.getSilent(`/onlineclassroom/${roomId}`);
                        if (latest.status !== pv.status) { pv.status = latest.status; render(); }
                    } catch { /* mất kết nối thoáng qua: lần kiểm tra sau sẽ bù */ }
                }, 5000);
            }

            el('pv-join').onclick = () => {
                if (!canJoin()) return;
                clearInterval(pv.pollTimer);
                stopMeter();
                overlay.hidden = true;
                placeholder.hidden = false;
                behind.forEach(node => { node.inert = false; });
                resolve({ stream: pv.stream, videoTrack: pv.videoTrack, audioTrack: pv.audioTrack, camOn: pv.camOn, micOn: pv.micOn });
            };

            render();
            acquire().then(() => el('pv-join').focus());
        });
    }

    /** Nhận luồng từ Màn hình xem trước làm luồng của phòng (thay cho việc xin quyền lần thứ hai).
     *  • Chủ trì: dùng nguyên luồng (camera + mic); bật/tắt theo lựa chọn.
     *  • Học viên: luồng của phòng chỉ có mic (kênh gửi camera là một transceiver riêng, xem connectToHost). Camera được
     *    giữ lại chỉ khi học viên đã bật nó ở bước xem trước; tắt thì dừng hẳn để đèn camera không sáng suốt buổi học. */
    function adoptPreviewMedia(prefs) {
        if (isHost) {
            localStream = prefs.stream || new MediaStream();
            cameraTrack = prefs.videoTrack || null;
            return;
        }
        localStream = new MediaStream(prefs.audioTrack ? [prefs.audioTrack] : []);
        if (prefs.videoTrack && !prefs.camOn) { prefs.videoTrack.stop(); cameraTrack = null; }
        else cameraTrack = prefs.videoTrack || null;
    }

    // ==================== MEDIA CỤC BỘ ====================
    function createPeerConnection(remoteConnectionId) {
        const pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });
        pc.onicecandidate = (e) => {
            if (e.candidate) connection.invoke('Signal', remoteConnectionId, 'candidate', JSON.stringify(e.candidate));
        };
        pc.ontrack = (e) => {
            if (isHost) return; // host tự override pc.ontrack riêng trong handleOfferAsHost, không dùng nhánh này
            if (e.streams[0]) hostStream = e.streams[0];
            else { hostStream ??= new MediaStream(); hostStream.addTrack(e.track); }
            playHostAudio();
            scheduleGallery();
        };
        return pc;
    }

    // ---------- Phía Viewer: chủ động tạo offer gửi tới host ----------
    async function connectToHost(host) {
        const pc = createPeerConnection(host.connectionId);
        upsertPeer(host).pc = pc;

        localStream.getTracks().forEach(t => pc.addTrack(t, localStream));
        pc.addTransceiver('video', { direction: 'recvonly' });
        myCamSender = pc.addTransceiver('video', { direction: 'sendonly' }).sender;

        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        await connection.invoke('Signal', host.connectionId, 'offer', JSON.stringify(offer));
        renderPeopleList();
    }

    // ---------- Phía Host: nhận offer từ viewer, phát video/mic của mình cho họ ----------
    async function handleOfferAsHost(fromConnectionId, offer) {
        const peer = peerList[fromConnectionId] || upsertPeer({ connectionId: fromConnectionId, fullName: 'Học viên', isHost: false });

        const pc = createPeerConnection(fromConnectionId);
        peer.pc = pc;
        localStream.getTracks().forEach(t => pc.addTrack(t, localStream));

        pc.ontrack = (e) => {
            if (e.track.kind === 'video') {
                peer.videoTrack = e.track;
                scheduleGallery();
                return;
            }
            let a = document.getElementById(`audio-${fromConnectionId}`);
            if (!a) {
                a = document.createElement('audio');
                a.id = `audio-${fromConnectionId}`;
                a.autoplay = true;
                document.body.appendChild(a);
            }
            a.srcObject = new MediaStream([e.track]);
        };

        await pc.setRemoteDescription(offer);
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        await connection.invoke('Signal', fromConnectionId, 'answer', JSON.stringify(answer));
        scheduleGallery();
        renderPeopleList();
    }

    // ==================== GIAO DIỆN ====================
    // ---------- Ảnh đại diện ----------
    function hueOf(name) { let h = 0; for (const ch of String(name || '')) h = (h * 31 + ch.codePointAt(0)) % 360; return h; }

    /** Ảnh đại diện tròn: chữ cái đầu (nền màu theo tên) nằm dưới, ảnh thật phủ lên trên. Ảnh lỗi thì bị gỡ đi, còn lại chữ cái. */
    function avatarMarkup(name, url) {
        return `<div class="gl-avatar" style="--hue:${hueOf(name)}"><span>${esc(initial(name))}</span>${url ? `<img src="${esc(url)}" alt="" decoding="async">` : ''}</div>`;
    }

    function buildAvatar(name, url) {
        const holder = document.createElement('div');
        holder.innerHTML = avatarMarkup(name, url);
        const avatar = holder.firstElementChild;
        avatar.querySelector('img')?.addEventListener('error', e => e.target.remove());
        return avatar;
    }

    // ==================== LƯỚI NGƯỜI THAM GIA ====================
    // Mỗi người một ô, màn hình chia theo số người như cuộc họp trực tuyến: 1 người choán cả khung, 2 người chia đôi,
    // 3 người chia ba, 4 người thành 2×2... Quá sức chứa thì ô cuối là "+N" (bấm để mở danh sách người tham gia).
    // Có người chia sẻ màn hình, hoặc người dùng ghim một ô, thì ô đó phóng to còn những người khác xếp thành dải bên cạnh.
    //
    // Về hình ảnh: kênh WebRTC là hình sao quanh Chủ trì — Chủ trì thấy hình của từng học viên, còn học viên chỉ nhận được
    // hình của Chủ trì (không thấy nhau). Học viên khác vì vậy hiện bằng ảnh đại diện cùng trạng thái mic/camera do máy
    // chủ báo, không phải khung đen.
    //
    // Các hàm khai báo bằng `function` (được hoist) vì sự kiện của hub có thể chạy trước khi các `const` phía dưới được khai báo.

    /** Gộp mình và mọi người khác thành một danh sách ô theo thứ tự hiển thị: Chủ trì đứng đầu; học viên thì tiếp đến là chính mình. */
    function buildMembers() {
        const audio = localStream?.getAudioTracks()[0];
        // Chủ trì: hình đang phát đi (camera hoặc màn hình). Học viên: màn hình đang chia sẻ, hoặc camera nếu đã bật.
        const ownTrack = isHost ? localStream?.getVideoTracks()[0] : (mySharing ? myScreenTrack : cameraTrack);
        const ownLive = ownTrack && ownTrack.readyState === 'live' ? ownTrack : null;
        const me = {
            key: 'self', isSelf: true, isHost, name: Auth.profile?.fullName || '', avatarUrl: Auth.profile?.avatarUrl || null,
            micOn: !!audio?.enabled, camOn: !!ownLive?.enabled, sharing: mySharing, source: ownLive,
        };
        const others = Object.values(peerList).map(p => ({
            key: p.connectionId, isSelf: false, isHost: p.isHost, name: p.fullName, avatarUrl: p.avatarUrl,
            micOn: p.micOn, camOn: p.camOn, sharing: p.sharing,
            // Chủ trì thấy hình của mọi học viên; học viên chỉ nhận được luồng của Chủ trì.
            source: isHost ? p.videoTrack : (p.isHost ? hostStream : null),
        }));
        const rest = others.filter(m => !m.isHost);
        if (isHost) return [me, ...rest];
        return [...others.filter(m => m.isHost), me, ...rest];
    }

    /** Tạo/cập nhật ghi nhận về một người từ dữ liệu hub gửi (ParticipantJoined, danh sách lúc vào phòng...). */
    function upsertPeer(p) {
        const peer = peerList[p.connectionId] || { pc: null, connectionId: p.connectionId, videoTrack: null };
        peer.fullName = p.fullName;
        peer.isHost = !!p.isHost;
        peer.avatarUrl = p.avatarUrl || null;
        peer.micOn = !!p.micOn;
        peer.camOn = !!p.camOn;
        peer.sharing = !!p.isSharing;
        peerList[p.connectionId] = peer;
        return peer;
    }

    /** Phát tiếng của Chủ trì qua thẻ <audio> riêng. Ô của Chủ trì trong lưới chỉ hiển thị hình (muted), nên tắt camera hay
     *  ô bị đổi chỗ cũng không làm mất tiếng giảng. */
    function playHostAudio() {
        if (!hostStream) return;
        let audio = document.getElementById('host-audio');
        if (!audio) {
            audio = document.createElement('audio');
            audio.id = 'host-audio';
            audio.autoplay = true;
            document.body.appendChild(audio);
        }
        if (audio.srcObject !== hostStream) audio.srcObject = hostStream;
        audio.play?.().catch(() => { /* trình duyệt chặn tự phát: sẽ phát khi người dùng chạm vào trang */ });
    }

    // ---------- Tính bố cục ----------
    /** Cách chia lưới tốt nhất (nhiều diện tích nhất cho mỗi ô) cho `count` ô trong khung `w`×`h`. Ô được co dãn theo
     *  ô lưới nhưng giữ tỉ lệ trong khoảng [GAL_MIN_ASPECT, GAL_MAX_ASPECT] — không dẹt như sợi chỉ, không cao như tờ giấy. */
    function bestGrid(count, w, h) {
        let best = null;
        for (let cols = 1; cols <= count; cols++) {
            const rows = Math.ceil(count / cols);
            const cellW = (w - GAL_GAP * (cols - 1)) / cols;
            const cellH = (h - GAL_GAP * (rows - 1)) / rows;
            if (cellW < 1 || cellH < 1) continue;
            let tileW = cellW, tileH = cellH;
            if (tileW / tileH > GAL_MAX_ASPECT) tileW = tileH * GAL_MAX_ASPECT;
            else if (tileW / tileH < GAL_MIN_ASPECT) tileH = tileW / GAL_MIN_ASPECT;
            if (!best || tileW * tileH > best.w * best.h * 1.001) best = { cols, rows, w: Math.floor(tileW), h: Math.floor(tileH) };
        }
        return best || { cols: 1, rows: count, w: 1, h: 1 };
    }

    /** Toạ độ từng ô của lưới: cả khối căn giữa khung, hàng cuối (thiếu ô) cũng căn giữa. */
    function gridRects(count, area, grid) {
        const rects = [];
        const blockH = grid.rows * grid.h + (grid.rows - 1) * GAL_GAP;
        const top = area.y + Math.max(0, (area.h - blockH) / 2);
        for (let row = 0; row < grid.rows; row++) {
            const inRow = Math.min(grid.cols, count - row * grid.cols);
            const rowW = inRow * grid.w + (inRow - 1) * GAL_GAP;
            const left = area.x + Math.max(0, (area.w - rowW) / 2);
            for (let col = 0; col < inRow; col++)
                rects.push({ x: Math.round(left + col * (grid.w + GAL_GAP)), y: Math.round(top + row * (grid.h + GAL_GAP)), w: grid.w, h: grid.h });
        }
        return rects;
    }

    /** Khung chứa được tối đa bao nhiêu ô mà mỗi ô vẫn đủ lớn để nhìn (không bao giờ dưới 3, để Chủ trì và chính mình luôn hiện). */
    function gridCapacity(total, area) {
        let fit = 1;
        for (let count = 2; count <= Math.min(total, GAL_MAX_TILES); count++) {
            const grid = bestGrid(count, area.w, area.h);
            if (grid.w < GAL_MIN_W || grid.h < GAL_MIN_H) break;
            fit = count;
        }
        return Math.max(fit, 3);
    }

    /** Ô được phóng to: người được ghim, nếu không thì người đang chia sẻ màn hình mà mình xem được (và chưa bị thu về lưới). */
    function pickSpotlight(members) {
        if (members.length < 2) return null;
        if (pinnedKey && members.some(m => m.key === pinnedKey)) return pinnedKey;
        const sharer = members.find(m => m.sharing && m.source && m.key !== dismissedKey);
        return sharer ? sharer.key : null;
    }

    /** Cắt danh sách cho vừa `slots` chỗ: thừa thì chừa chỗ cuối cho ô "+N". */
    function fitSlots(list, slots) {
        if (list.length <= slots) return { shown: list, more: 0 };
        const shown = list.slice(0, Math.max(0, slots - 1));
        return { shown, more: list.length - shown.length };
    }

    /** Kế hoạch hiển thị: danh sách { member | more, x, y, w, h } và khoá của ô đang phóng to (nếu có). */
    function planLayout(members, area) {
        const spotKey = pickSpotlight(members);
        if (!spotKey) {
            const { shown, more } = fitSlots(members, gridCapacity(members.length, area));
            const count = shown.length + (more ? 1 : 0);
            const rects = gridRects(count, area, bestGrid(count, area.w, area.h));
            const items = shown.map((member, i) => ({ member, ...rects[i] }));
            if (more) items.push({ more, ...rects[count - 1] });
            return { items, spotKey: null };
        }

        // Phóng to một ô, những người còn lại thành dải: bên phải khi khung ngang, bên dưới khi khung dọc (điện thoại).
        const spot = members.find(m => m.key === spotKey);
        const rest = members.filter(m => m !== spot);
        const landscape = area.w >= area.h;
        let main, slots, slotW, slotH;
        if (landscape) {
            slotW = Math.max(150, Math.min(220, Math.round(area.w * 0.2)));
            slotH = Math.round(slotW * 0.72);
            main = { x: area.x, y: area.y, w: area.w - slotW - GAL_GAP, h: area.h };
            slots = Math.max(2, Math.floor((area.h + GAL_GAP) / (slotH + GAL_GAP)));
        } else {
            slotH = Math.max(88, Math.min(130, Math.round(area.h * 0.2)));
            slotW = Math.round(slotH * 1.33);
            main = { x: area.x, y: area.y, w: area.w, h: area.h - slotH - GAL_GAP };
            slots = Math.max(2, Math.floor((area.w + GAL_GAP) / (slotW + GAL_GAP)));
        }
        const { shown, more } = fitSlots(rest, slots);
        const stripItems = [...shown.map(member => ({ member })), ...(more ? [{ more }] : [])];
        const stripSpan = stripItems.length * (landscape ? slotH : slotW) + (stripItems.length - 1) * GAL_GAP;
        stripItems.forEach((item, i) => {
            if (landscape) Object.assign(item, { x: area.x + area.w - slotW, y: area.y + i * (slotH + GAL_GAP), w: slotW, h: slotH });
            else Object.assign(item, {
                x: Math.round(area.x + Math.max(0, (area.w - stripSpan) / 2) + i * (slotW + GAL_GAP)), y: area.y + area.h - slotH, w: slotW, h: slotH,
            });
        });
        return { items: [{ member: spot, ...main }, ...stripItems], spotKey };
    }

    // ---------- Vẽ ----------
    function createTile(key, isMore) {
        const tile = document.createElement(isMore ? 'button' : 'div');
        tile.className = 'gl-tile' + (isMore ? ' gl-more' : '');
        tile._key = key;
        if (isMore) {
            tile.type = 'button';
            tile.innerHTML = '<b></b><span></span>';
            tile.addEventListener('click', openPeoplePanel);
        } else {
            tile.setAttribute('role', 'group');
            tile.innerHTML = `
                <video class="gl-video" autoplay playsinline muted hidden></video>
                <div class="gl-face"></div>
                <span class="gl-mic" hidden><i class="ph-fill ph-microphone-slash"></i></span>
                <button type="button" class="gl-pin"><i class="ph ph-push-pin"></i></button>
                <div class="gl-name"><i class="ph-fill ph-crown-simple" hidden></i><i class="ph ph-monitor-arrow-up" hidden></i><span></span></div>`;
            tile.querySelector('.gl-pin').addEventListener('click', () => togglePin(tile._key));
        }
        document.getElementById('cr-gallery').appendChild(tile);
        tileMap.set(key, tile);
        return tile;
    }

    function paintTile(tile, m, spotKey, canPin) {
        const hasVideo = !!m.source && (m.camOn || m.sharing);
        const label = m.isSelf ? `${m.name} (${t('classroom.ban', 'Bạn')})` : m.name;

        // Ảnh đại diện chỉ dựng lại khi đổi tên hoặc ảnh (tránh tải lại ảnh mỗi lần vẽ).
        const faceSig = `${m.name}\u0001${m.avatarUrl || ''}`;
        const face = tile.querySelector('.gl-face');
        if (tile._faceSig !== faceSig) { tile._faceSig = faceSig; face.replaceChildren(buildAvatar(m.name, m.avatarUrl)); }
        face.hidden = hasVideo;

        // Hình: chỉ gắn lại khi nguồn đổi, để video không bị khựng.
        const video = tile.querySelector('.gl-video');
        const source = hasVideo ? m.source : null;
        if (tile._source !== source) {
            tile._source = source;
            video.srcObject = !source ? null : source instanceof MediaStream ? source : new MediaStream([source]);
            video.hidden = !source;
            if (source) video.play?.().catch(() => { });
        }
        video.classList.toggle('mirror', m.isSelf && !m.sharing); // camera của chính mình soi gương cho tự nhiên; màn hình chia sẻ thì không
        tile.classList.toggle('share', m.sharing && hasVideo);
        tile.classList.toggle('pinned', tile._key === pinnedKey);

        const nameText = tile.querySelector('.gl-name span');
        if (nameText.textContent !== label) nameText.textContent = label;
        tile.querySelector('.ph-crown-simple').hidden = !m.isHost;
        tile.querySelector('.ph-crown-simple').title = t('classroom.chuTri', 'Chủ trì');
        tile.querySelector('.gl-name .ph-monitor-arrow-up').hidden = !m.sharing;
        const mic = tile.querySelector('.gl-mic');
        mic.hidden = m.micOn;
        mic.title = t('classroom.glMicOff', 'Mic tắt');

        const zoomed = tile._key === spotKey;
        const pin = tile.querySelector('.gl-pin');
        pin.hidden = !canPin;
        const pinText = zoomed ? t('classroom.glUnpin', 'Thu về lưới') : t('classroom.glPin', 'Ghim');
        pin.title = pinText;
        pin.setAttribute('aria-label', `${pinText}: ${label}`);
        pin.setAttribute('aria-pressed', String(zoomed));
        pin.firstElementChild.className = `ph ${zoomed ? 'ph-push-pin-slash' : 'ph-push-pin'}`;

        const parts = [label];
        if (m.isHost) parts.push(t('classroom.chuTri', 'Chủ trì'));
        if (!m.micOn) parts.push(t('classroom.glMicOff', 'Mic tắt'));
        if (!m.camOn && !m.sharing) parts.push(t('classroom.glCamOff', 'Camera tắt')); // theo trạng thái người đó báo, không theo việc mình có nhận được hình hay không
        if (m.sharing) parts.push(t('classroom.glSharing', 'Đang chia sẻ màn hình'));
        tile.setAttribute('aria-label', parts.join(', '));
    }

    function paintMoreTile(tile, count) {
        tile.querySelector('b').textContent = `+${count}`;
        tile.querySelector('span').textContent = t('classroom.glMore', '{n} người khác', { n: count });
        tile.setAttribute('aria-label', t('classroom.glMoreAria', 'Xem thêm {n} người khác trong danh sách người tham gia', { n: count }));
    }

    function placeTile(tile, rect) {
        const firstTime = !tile.classList.contains('ready');
        tile.style.width = `${rect.w}px`;
        tile.style.height = `${rect.h}px`;
        tile.style.transform = `translate(${rect.x}px, ${rect.y}px)`;
        tile.style.setProperty('--face', `${Math.round(Math.max(36, Math.min(120, Math.min(rect.w, rect.h) * 0.42)))}px`);
        tile.classList.toggle('compact', rect.h < 130 || rect.w < 190);
        // Ô mới đặt thẳng vào chỗ của nó; chỉ từ lần sau mới trượt mượt khi bố cục đổi.
        if (firstTime) { void tile.offsetWidth; tile.classList.add('ready'); }
    }

    function renderGallery() {
        galleryFrame = 0;
        if (!joined) return;
        const gallery = document.getElementById('cr-gallery');
        document.getElementById('cr-placeholder').hidden = true;
        const area = { x: GAL_PAD, y: GAL_PAD, w: gallery.clientWidth - 2 * GAL_PAD, h: gallery.clientHeight - 2 * GAL_PAD };
        if (area.w < 40 || area.h < 40) return;

        const members = buildMembers();
        if (pinnedKey && !members.some(m => m.key === pinnedKey)) pinnedKey = null;
        if (dismissedKey && !members.some(m => m.key === dismissedKey)) dismissedKey = null;
        const { items, spotKey } = planLayout(members, area);
        currentSpotKey = spotKey;

        const alive = new Set();
        for (const item of items) {
            const key = item.more ? GAL_MORE_KEY : item.member.key;
            alive.add(key);
            const tile = tileMap.get(key) || createTile(key, !!item.more);
            if (item.more) paintMoreTile(tile, item.more);
            else paintTile(tile, item.member, spotKey, members.length > 1);
            placeTile(tile, item);
        }
        for (const [key, tile] of tileMap) {
            if (alive.has(key)) continue;
            const video = tile.querySelector('video');
            if (video) video.srcObject = null;
            tile.remove();
            tileMap.delete(key);
        }
    }

    /** Vẽ lại lưới ở khung hình kế tiếp — gom nhiều thay đổi liên tiếp (vào phòng, bật mic, đổi cỡ khung...) thành một lần vẽ. */
    function scheduleGallery() {
        if (!galleryFrame) galleryFrame = requestAnimationFrame(renderGallery);
    }

    /** Ghim / thu về lưới. Ô đang phóng to (do ghim hay do chia sẻ màn hình) bấm lần nữa thì thu về lưới. */
    function togglePin(key) {
        if (currentSpotKey === key) { pinnedKey = null; dismissedKey = key; }
        else { pinnedKey = key; dismissedKey = null; }
        scheduleGallery();
    }

    function openPeoplePanel() {
        document.querySelector('.cr-side-tabs button[data-panel="people"]')?.click();
        setSidePanel(true);
    }

    function renderPeopleList() {
        const list = Object.values(peerList);
        const total = list.length + 1;
        document.getElementById('cr-people-count').textContent = total;
        document.getElementById('cr-people-chip-count').textContent = total;
        const hostTag = ` · ${esc(t('classroom.chuTri', 'Chủ trì'))}`;
        const row = (name, avatarUrl, text) => `<div class="cr-participant">${avatarMarkup(name, avatarUrl)}<div>${text}</div></div>`;
        const me = Auth.profile;
        document.getElementById('cr-people-list').innerHTML = [
            row(me?.fullName, me?.avatarUrl, `${esc(me?.fullName)} (${esc(t('classroom.ban', 'Bạn'))})${isHost ? hostTag : ''}`),
            ...list.map(p => row(p.fullName, p.avatarUrl,
                `${esc(p.fullName)}${p.isHost ? hostTag : ''}${p.micOn ? ` <span class="tags">${esc(t('classroom.micBat', '· mic bật'))}</span>` : ''}${p.camOn ? ` <span class="tags">${esc(t('classroom.camBat', '· cam bật'))}</span>` : ''}`))
        ].join('');
    }

    function appendChat(fullName, message, time) {
        const log = document.getElementById('cr-chat-log');
        document.getElementById('cr-chat-empty')?.remove();
        const mine = fullName === Auth.profile?.fullName;
        log.insertAdjacentHTML('beforeend',
            `<div class="cr-chat-msg${mine ? ' mine' : ''}"><b>${esc(fullName)}</b><span class="time">${formatDate(time, true)}</span><div>${esc(message)}</div></div>`);
        log.scrollTop = log.scrollHeight;

        // Khung chat đang đóng: báo bằng chấm đỏ trên nút ở thanh trên (tin của chính mình thì không).
        if (!mine && document.getElementById('cr-side').classList.contains('collapsed')) {
            unreadCount++;
            const badge = document.getElementById('cr-unread');
            badge.textContent = unreadCount > 9 ? '9+' : String(unreadCount);
            badge.hidden = false;
        }
    }

    function sendChat() {
        const input = document.getElementById('cr-chat-text');
        const message = input.value.trim();
        if (!message) return;
        connection.invoke('SendChatMessage', roomId, message);
        input.value = '';
    }

    /** Sau khi vào phòng: lấy lại thông tin buổi học (giờ bắt đầu THẬT, trạng thái Đang diễn ra — dữ liệu lúc mở trang có
     *  thể đã cũ nếu học viên chờ ở Màn hình xem trước) rồi chạy đồng hồ "đã diễn ra" trên thanh trên. */
    async function startMeetingClock() {
        let startedAt = Date.now();
        try {
            const fresh = await API.getSilent(`/onlineclassroom/${roomId}`);
            room = fresh;
            document.getElementById('cr-status').innerHTML = statusBadge(fresh.status);
            if (fresh.actualStartAt) startedAt = new Date(fresh.actualStartAt).getTime();
        } catch { /* mất kết nối thoáng qua: đếm từ lúc vào phòng */ }

        const box = document.getElementById('cr-elapsed');
        const text = document.getElementById('cr-elapsed-text');
        const paint = () => { text.textContent = formatClockMs(Date.now() - startedAt); };
        box.hidden = false;
        paint();
        clearInterval(meetingTimer);
        meetingTimer = setInterval(paint, 1000);
    }

    /** Một nút của bàn điều khiển: biểu tượng + nhãn trạng thái bên dưới; aria-label/title nói THAO TÁC khi bấm. */
    function ctrlButton(id, { cls = '', icon, label, aria, hidden = false }) {
        return `<button type="button" class="cr-ctrl-btn ${cls}" id="${id}"${hidden ? ' hidden' : ''} title="${esc(aria)}" aria-label="${esc(aria)}">
            <i class="ph ${icon}"></i><span class="cr-ctrl-label">${esc(label)}</span></button>`;
    }

    function renderControls() {
        const box = document.getElementById('cr-controls');
        box.innerHTML = [
            ctrlButton('btn-mic', { icon: 'ph-microphone-slash', label: t('classroom.ctlMicOff', 'Mic tắt'), aria: t('classroom.pvMicOn', 'Bật micro') }),
            ctrlButton('btn-cam', { icon: 'ph-video-camera-slash', label: t('classroom.ctlCamOff', 'Cam tắt'), aria: t('classroom.pvCamOn', 'Bật camera') }),
            '<span class="cr-ctrl-sep" aria-hidden="true"></span>',
            ctrlButton('btn-share', { icon: 'ph-monitor-arrow-up', label: t('classroom.ctlShare', 'Chia sẻ'), aria: t('classroom.chiaSeManHinh', 'Chia sẻ màn hình') }),
            isHost ? '<span class="cr-ctrl-sep" aria-hidden="true"></span>' : '',
            isHost ? ctrlButton('btn-record', { icon: 'ph-record', label: t('classroom.ctlRecord', 'Ghi hình'), aria: t('classroom.ghiHinhBuoiHocYeuCauChoPhepQua', 'Ghi hình buổi học (yêu cầu cho phép quay tab này)') }) : '',
            isHost ? ctrlButton('btn-rec-pause', { icon: 'ph-pause', label: t('classroom.ctlRecPause', 'Tạm dừng'), aria: t('classroom.recPause', 'Tạm dừng ghi hình'), hidden: true }) : '',
            isHost ? ctrlButton('btn-rec-stop', { cls: 'off', icon: 'ph-stop', label: t('classroom.ctlRecStop', 'Dừng ghi'), aria: t('classroom.recStop', 'Dừng ghi hình'), hidden: true }) : '',
            '<span class="cr-ctrl-sep" aria-hidden="true"></span>',
            ctrlButton('btn-leave', { cls: 'cr-leave-btn', icon: 'ph-phone-x',
                label: isHost ? t('classroom.ctlEnd', 'Kết thúc') : t('classroom.roiPhong', 'Rời phòng'),
                aria: isHost ? t('classroom.ctlEndAria', 'Kết thúc buổi học') : t('classroom.roiPhong', 'Rời phòng') }),
        ].join('');

        // Trạng thái ban đầu: mic tắt; camera theo vai trò (chủ trì bật). syncMicButton/syncCamButton chỉnh lại theo thiết bị thật sau khi vào phòng.
        paintMic(false);
        paintCam(isHost);
        paintShare(false);
        // Ghi hình cần buổi học đã "Đang diễn ra" (server từ chối nếu chưa): khóa nút tới khi vào phòng xong (xem 'Joined').
        if (isHost) document.getElementById('btn-record').disabled = true;
        // Mic, camera, chia sẻ màn hình cũng vậy: bấm trước khi vào phòng thì lệnh báo trạng thái tới hub khi mình chưa có mặt
        // nên bị bỏ qua, và mọi người trong phòng thấy sai (mic bật mà ô vẫn báo tắt).
        ['btn-mic', 'btn-cam', 'btn-share'].forEach(id => { document.getElementById(id).disabled = true; });

        document.getElementById('btn-mic').addEventListener('click', toggleMic);
        document.getElementById('btn-cam').addEventListener('click', toggleCamera);
        document.getElementById('btn-share').addEventListener('click', toggleScreenShare);
        if (isHost) {
            document.getElementById('btn-record').addEventListener('click', startRecording);
            document.getElementById('btn-rec-pause').addEventListener('click', toggleRecordingPause);
            document.getElementById('btn-rec-stop').addEventListener('click', async () => {
                toastOk(t('classroom.dangLuuBanGhiBuoiHoc', 'Đang lưu bản ghi buổi học...'));
                await stopAndUploadRecording();
            });
            syncRecordButtons();
        }
        document.getElementById('btn-leave').addEventListener('click', async () => {
            const ok = await confirmDialog(isHost ? 'Kết thúc buổi học cho tất cả mọi người?' : t('classroom.roiKhoiPhongHoc', 'Rời khỏi phòng học?'),
                { danger: true, okText: isHost ? 'Kết thúc' : t('classroom.roiPhong', 'Rời phòng') });
            if (!ok) return;
            document.getElementById('btn-leave').disabled = true;
            if (mediaRecorder && mediaRecorder.state !== 'inactive') {
                toastOk(t('classroom.dangLuuBanGhiBuoiHoc', 'Đang lưu bản ghi buổi học...'));
                await stopAndUploadRecording();
            }
            if (isHost) await connection.invoke('EndRoom', roomId);
            cleanupAndLeave();
            location.href = '/pages/online-class/online-class.html';
        });
    }

    // ---- Vẽ trạng thái của từng nút (khai báo bằng `function` để dùng được ngay từ renderControls) ----
    function paintToggle(id, on, { iconOn, iconOff, labelOn, labelOff, ariaOn, ariaOff }) {
        const btn = document.getElementById(id);
        if (!btn) return;
        btn.classList.toggle('off', !on);
        btn.classList.toggle('active', on);
        btn.setAttribute('aria-pressed', String(on));
        btn.querySelector('i').className = `ph ${on ? iconOn : iconOff}`;
        btn.querySelector('.cr-ctrl-label').textContent = on ? labelOn : labelOff;
        btn.title = on ? ariaOn : ariaOff;
        btn.setAttribute('aria-label', on ? ariaOn : ariaOff);
        scheduleGallery(); // ô của mình hiện/ẩn hình + biểu tượng mic theo đúng trạng thái này
    }

    function paintMic(on) {
        paintToggle('btn-mic', on, {
            iconOn: 'ph-microphone', iconOff: 'ph-microphone-slash',
            labelOn: t('classroom.ctlMicOn', 'Mic bật'), labelOff: t('classroom.ctlMicOff', 'Mic tắt'),
            ariaOn: t('classroom.pvMicOff', 'Tắt micro'), ariaOff: t('classroom.pvMicOn', 'Bật micro'),
        });
    }

    function paintCam(on) {
        paintToggle('btn-cam', on, {
            iconOn: 'ph-video-camera', iconOff: 'ph-video-camera-slash',
            labelOn: t('classroom.ctlCamOn', 'Cam bật'), labelOff: t('classroom.ctlCamOff', 'Cam tắt'),
            ariaOn: t('classroom.pvCamOff', 'Tắt camera'), ariaOff: t('classroom.pvCamOn', 'Bật camera'),
        });
    }

    /** Chia sẻ màn hình không có trạng thái "tắt đỏ" — chỉ sáng lên khi đang chia sẻ. toggleScreenShare đọc class `active` để biết đang chia sẻ hay không. */
    function paintShare(on) {
        mySharing = on;
        if (on && dismissedKey === 'self') dismissedKey = null;
        scheduleGallery();
        const btn = document.getElementById('btn-share');
        if (!btn) return;
        btn.classList.toggle('active', on);
        btn.setAttribute('aria-pressed', String(on));
        btn.querySelector('.cr-ctrl-label').textContent = on ? t('classroom.ctlSharing', 'Đang chia sẻ') : t('classroom.ctlShare', 'Chia sẻ');
        const aria = on ? t('classroom.ctlStopShare', 'Dừng chia sẻ màn hình') : t('classroom.chiaSeManHinh', 'Chia sẻ màn hình');
        btn.title = aria;
        btn.setAttribute('aria-label', aria);
    }

    function toggleMic() {
        const track = localStream?.getAudioTracks()[0];
        if (!track) return;
        track.enabled = !track.enabled;
        syncMicButton();
        connection.invoke('ToggleMic', roomId, track.enabled);
    }

    /** Đồng bộ nút camera với trạng thái thật: chủ trì theo track video của luồng; học viên theo camera đã xin quyền.
     *  Chủ trì không có camera (bị từ chối quyền / không có thiết bị) thì khóa nút. */
    function syncCamButton() {
        const btn = document.getElementById('btn-cam');
        if (!btn) return;
        const on = isHost ? !!localStream?.getVideoTracks()[0]?.enabled : !!cameraTrack?.enabled;
        paintCam(on);
        if (isHost) btn.disabled = !localStream?.getVideoTracks()[0];
    }

    function syncMicButton() {
        const track = localStream?.getAudioTracks()[0];
        const btn = document.getElementById('btn-mic');
        if (!btn) return;
        paintMic(!!track?.enabled);
        btn.disabled = !track;   // không có mic (bị từ chối quyền / không có thiết bị) thì khóa nút
    }

    async function toggleCamera() {
        let camOn;

        if (isHost) {
            const track = localStream?.getVideoTracks()[0];
            if (!track) return;
            track.enabled = !track.enabled;
            camOn = track.enabled;
            connection.invoke('ToggleCam', roomId, camOn); // học viên khác cần biết để hiện ảnh đại diện thay cho khung đen
        } else {
            if (!cameraTrack) {
                try {
                    const camStream = await navigator.mediaDevices.getUserMedia({ video: true });
                    cameraTrack = camStream.getVideoTracks()[0];
                    localStream.addTrack(cameraTrack);
                    if (myCamSender) await myCamSender.replaceTrack(cameraTrack);
                } catch {
                    toastErr(t('classroom.khongTruyCapDuocCameraKiemTraQ', 'Không truy cập được camera. Kiểm tra quyền trình duyệt.'));
                    return;
                }
            } else {
                cameraTrack.enabled = !cameraTrack.enabled;
            }
            camOn = cameraTrack.enabled;
            connection.invoke('ToggleCam', roomId, camOn);
        }

        paintCam(camOn);
    }

    async function toggleScreenShare() {
        const btn = document.getElementById('btn-share');
        const sharing = btn.classList.contains('active');
        try {
            if (isHost) {
                if (!sharing) {
                    const screenStream = await navigator.mediaDevices.getDisplayMedia({ video: true });
                    const screenTrack = screenStream.getVideoTracks()[0];
                    replaceOutgoingVideoTrack(screenTrack);
                    screenTrack.onended = () => {
                        replaceOutgoingVideoTrack(cameraTrack);
                        paintShare(false);
                        connection.invoke('ToggleScreenShare', roomId, false);
                    };
                    paintShare(true);
                    connection.invoke('ToggleScreenShare', roomId, true);
                } else {
                    replaceOutgoingVideoTrack(cameraTrack);
                    paintShare(false);
                    connection.invoke('ToggleScreenShare', roomId, false);
                }
            } else {
                await toggleScreenShareViewer(btn, sharing);
            }
        } catch { /* người dùng hủy hộp thoại chọn màn hình */ }
    }

    async function toggleScreenShareViewer(btn, sharing) {
        if (!sharing) {
            const screenStream = await navigator.mediaDevices.getDisplayMedia({ video: true });
            myScreenTrack = screenStream.getVideoTracks()[0];
            if (myCamSender) await myCamSender.replaceTrack(myScreenTrack);
            myScreenTrack.onended = () => stopViewerScreenShare();
            paintShare(true);
            connection.invoke('ToggleCam', roomId, true);
            connection.invoke('ToggleScreenShare', roomId, true);
        } else {
            stopViewerScreenShare();
        }
    }

    function stopViewerScreenShare() {
        myScreenTrack?.stop();
        myScreenTrack = null;
        paintShare(false);

        const camOn = !!cameraTrack?.enabled;
        if (myCamSender) myCamSender.replaceTrack(camOn ? cameraTrack : null);
        connection.invoke('ToggleCam', roomId, camOn);
        connection.invoke('ToggleScreenShare', roomId, false);
        paintCam(camOn);
    }

    function replaceOutgoingVideoTrack(newTrack) {
        if (!newTrack) return;
        Object.values(peerList).forEach(p => {
            const sender = p.pc?.getSenders().find(s => s.track && s.track.kind === 'video');
            if (sender) sender.replaceTrack(newTrack);
        });

        const oldTrack = localStream.getVideoTracks()[0];
        if (oldTrack && oldTrack !== newTrack) {
            localStream.removeTrack(oldTrack);
            if (oldTrack !== cameraTrack) oldTrack.stop(); // dừng track màn hình chia sẻ cũ; giữ track camera để dùng lại
        }
        if (!localStream.getVideoTracks().includes(newTrack)) localStream.addTrack(newTrack);
        scheduleGallery();
    }

    // ==================== GHI HÌNH BUỔI HỌC (chỉ Chủ trì điều khiển) ====================
    // Trình duyệt của Chủ trì thật sự quay (MediaRecorder); máy chủ giữ đồng hồ ghi hình chuẩn và phát trạng thái
    // cho cả phòng. Bản ghi: Ghi → (Tạm dừng ⇄ Tiếp tục)* → Dừng ghi. Tạm dừng giữ MỘT tệp, đoạn tạm dừng bị cắt khỏi
    // video. Dừng ghi chốt tệp; bấm Ghi lần nữa tạo Bản ghi mới.
    function pickRecorderMimeType() {
        if (typeof MediaRecorder === 'undefined') return null;
        const candidateList = ['video/webm;codecs=vp9,opus', 'video/webm;codecs=vp8,opus', 'video/webm'];
        return candidateList.find(t => MediaRecorder.isTypeSupported?.(t)) || null;
    }

    /** Trạng thái ghi hình mới từ server (hoặc lúc vừa vào phòng): cập nhật huy hiệu, đồng hồ và các nút của Chủ trì. */
    function applyRecordingState({ state, elapsedMs }) {
        const changed = state !== recState;
        recState = state;
        recBaseMs = elapsedMs || 0;
        recBaseAt = performance.now();

        const badge = document.getElementById('cr-rec-badge');
        badge.classList.toggle('show', state !== 'Idle');
        badge.classList.toggle('paused', state === 'Paused');
        // Viền khung hình đổi màu theo trạng thái ghi (đỏ đang ghi, hổ phách tạm dừng) — ai trong phòng cũng thấy.
        const wrap = document.querySelector('.cr-wrap');
        wrap.classList.toggle('is-recording', state === 'Recording');
        wrap.classList.toggle('is-paused', state === 'Paused');
        const label = state === 'Paused' ? t('classroom.recBadgePaused', 'Tạm dừng ghi') : t('classroom.recBadgeRecording', 'Đang ghi');
        document.getElementById('cr-rec-label').textContent = label;
        if (changed) {
            document.getElementById('cr-rec-announce').textContent =
                state === 'Idle' ? t('classroom.recAnnounceStopped', 'Đã dừng ghi hình') : label;
        }

        clearInterval(recTimerInterval);
        recTimerInterval = null;
        paintRecordingTime();
        if (state === 'Recording') recTimerInterval = setInterval(paintRecordingTime, 500);
        syncRecordButtons();
    }

    function paintRecordingTime() {
        const ms = recBaseMs + (recState === 'Recording' ? performance.now() - recBaseAt : 0);
        document.getElementById('cr-rec-time').textContent = formatClockMs(ms);
    }

    /** Nút của Chủ trì theo trạng thái: chưa ghi → "Ghi"; đang ghi → "Tạm dừng" + "Dừng ghi"; tạm dừng → "Tiếp tục" + "Dừng ghi". */
    function syncRecordButtons() {
        if (!isHost) return;
        const start = document.getElementById('btn-record');
        const pause = document.getElementById('btn-rec-pause');
        const stop = document.getElementById('btn-rec-stop');
        if (!start || !pause || !stop) return;

        start.hidden = recState !== 'Idle';
        pause.hidden = stop.hidden = recState === 'Idle';

        const paused = recState === 'Paused';
        pause.classList.toggle('active', paused);
        pause.querySelector('i').className = `ph ph-${paused ? 'play' : 'pause'}`;
        pause.querySelector('.cr-ctrl-label').textContent = paused ? t('classroom.ctlRecResume', 'Tiếp tục') : t('classroom.ctlRecPause', 'Tạm dừng');
        const pauseLabel = paused ? t('classroom.recResume', 'Tiếp tục ghi hình') : t('classroom.recPause', 'Tạm dừng ghi hình');
        pause.title = pauseLabel;
        pause.setAttribute('aria-label', pauseLabel);
    }

    async function startRecording() {
        if (!isHost || !localStream) return;
        if (mediaRecorder && mediaRecorder.state !== 'inactive') return; // đang ghi rồi, bỏ qua lần bấm thêm

        const mimeType = pickRecorderMimeType();
        if (!mimeType) {
            toastErr(t('classroom.trinhDuyetNayKhongHoTroGhiHinh', 'Trình duyệt này không hỗ trợ ghi hình buổi học tự động.'));
            return;
        }
        if (!navigator.mediaDevices?.getDisplayMedia) {
            toastErr(t('classroom.trinhDuyetNayKhongHoTroQuayLai', 'Trình duyệt này không hỗ trợ quay lại giao diện phòng học.'));
            return;
        }

        try {
            displayStream = await navigator.mediaDevices.getDisplayMedia({
                video: { frameRate: 15 },
                audio: false,
                preferCurrentTab: true,   // Chrome: tự chọn sẵn "tab này" trong hộp thoại xin quyền
                selfBrowserSurface: 'include'
            });
        } catch {
            toastErr(t('classroom.canChoPhepQuayTabNayThiMoiGhiH', 'Cần cho phép quay tab này thì mới ghi hình được buổi học. Bấm "Ghi hình" để thử lại.'));
            return;
        }

        try {
            const started = await API.postSilent(`/onlineclassroom/${roomId}/recording/start`);
            recordingId = started.id;
        } catch (ex) {
            toastErr(t('classroom.khongTaoDuocBanGhiTrenServer', 'Không tạo được bản ghi trên server: ') + ex.message);
            displayStream.getTracks().forEach(t => t.stop());
            displayStream = null;
            return;
        }

        const micTrack = localStream.getAudioTracks()[0];
        const compositeStream = new MediaStream([...displayStream.getVideoTracks(), ...(micTrack ? [micTrack] : [])]);

        try {
            mediaRecorder = new MediaRecorder(compositeStream, { mimeType });
        } catch (ex) {
            mediaRecorder = null;
            displayStream.getTracks().forEach(t => t.stop());
            displayStream = null;
            toastErr(t('classroom.khongTheBatDauGhiHinh', 'Không thể bắt đầu ghi hình: ') + (ex.message || t('classroom.loiKhongXacDinh', 'lỗi không xác định.')));
            return;
        }

        // Người dùng có thể tự bấm "Dừng chia sẻ" trên thanh thông báo của trình duyệt bất cứ lúc nào -> coi như dừng ghi hình.
        displayStream.getVideoTracks()[0].onended = () => {
            if (mediaRecorder && mediaRecorder.state !== 'inactive') {
                toastErr(t('classroom.daDungChiaSeManHinhDungGhiHinh', 'Đã dừng chia sẻ màn hình — dừng ghi hình buổi học.'));
                stopAndUploadRecording();
            }
        };

        mediaRecorder.ondataavailable = (e) => { if (e.data && e.data.size > 0) queueChunkUpload(e.data); };
        mediaRecorder.onerror = () => toastErr(t('classroom.ghiHinhBuoiHocBiGianDoan', 'Ghi hình buổi học bị gián đoạn.'));
        recordingStartAt = Date.now();
        recActiveMs = 0;
        recResumedAt = Date.now();
        mediaRecorder.start(5000); // cắt theo từng đoạn 5 giây, mỗi đoạn gửi lên server ngay
        // Máy chủ chạy đồng hồ ghi hình và phát trạng thái cho cả phòng (xem connection.on('RecordingState', ...)).
        connection.invoke('StartRecording', roomId, recordingId);
    }

    /** Tạm dừng khi đang ghi, tiếp tục khi đang tạm dừng — cùng một nút. Đoạn tạm dừng không vào video. */
    function toggleRecordingPause() {
        if (!mediaRecorder) return;
        if (mediaRecorder.state === 'recording') {
            mediaRecorder.pause();
            recActiveMs += Date.now() - recResumedAt; // chốt đoạn vừa quay xong
            connection.invoke('PauseRecording', roomId);
        } else if (mediaRecorder.state === 'paused') {
            mediaRecorder.resume();
            recResumedAt = Date.now();
            connection.invoke('ResumeRecording', roomId);
        }
    }

    function queueChunkUpload(blob) {
        uploadQueue = uploadQueue.then(async () => {
            const file = new File([blob], 'chunk.webm', { type: blob.type });
            try {
                await API.postFileSilent(`/onlineclassroom/${roomId}/recording/${recordingId}/chunk`, file);
            } catch (ex) {
                console.error(t('classroom.loiGuiDoanGhiHinh', 'Lỗi gửi đoạn ghi hình:'), ex.message);
            }
        });
    }

    /** Dừng ghi: chốt tệp (gửi nốt các đoạn, báo thời lượng thật không tính đoạn tạm dừng), rồi báo máy chủ dừng đồng hồ. */
    function stopAndUploadRecording() {
        return new Promise((resolve) => {
            if (!mediaRecorder || mediaRecorder.state === 'inactive') { resolve(); return; }

            // Đang quay dở một đoạn thì cộng nốt đoạn đó; đang tạm dừng thì đoạn cuối đã được cộng lúc bấm Tạm dừng.
            if (mediaRecorder.state === 'recording') recActiveMs += Date.now() - recResumedAt;
            const finishedRecordingId = recordingId;
            const durationMs = recActiveMs;
            const recorder = mediaRecorder;

            recorder.onstop = async () => {
                displayStream?.getTracks().forEach(t => t.stop());
                displayStream = null;
                await uploadQueue;

                if (finishedRecordingId) {
                    try { await API.postSilent(`/onlineclassroom/${roomId}/recording/${finishedRecordingId}/finish`, { durationMs }); }
                    catch (ex) { console.error(t('classroom.loiVaThoiLuongBanGhi', 'Lỗi vá thời lượng bản ghi:'), ex.message); }
                }
                // Báo máy chủ SAU khi tệp đã chốt: Diễn biến ghi "dừng ghi", huy hiệu của cả phòng tắt.
                try { await connection.invoke('StopRecording', roomId); } catch { /* mất kết nối: máy chủ tự chốt khi buổi học kết thúc */ }

                mediaRecorder = null;
                recordingId = null;
                recordingStartAt = null;
                toastOk(t('classroom.daLuuBanGhiBuoiHoc', 'Đã lưu bản ghi buổi học.'));
                resolve();
            };
            recorder.stop();
        });
    }

    function cleanupAndLeave() {
        Object.values(peerList).forEach(p => p.pc?.close());
        localStream?.getTracks().forEach(t => t.stop());
        displayStream?.getTracks().forEach(t => t.stop());
        clearInterval(recTimerInterval);
        clearInterval(meetingTimer);
        cancelAnimationFrame(galleryFrame);
        try { connection.stop(); } catch { }
    }

    window.addEventListener('beforeunload', cleanupAndLeave);
})();
