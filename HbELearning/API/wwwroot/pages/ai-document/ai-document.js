// File: wwwroot/pages/ai-document/ai-document.js
(async function () {
    // Chờ /locales/*.json tải xong rồi mới dựng giao diện — t() là hàm đồng bộ.
    await Lang.ready;

    if (!Auth.require(['Admin'], 'ai-document.view')) return;
    mountLayout('ai-document',
        t('aidoc.title', 'Tài liệu AI'),
        t('aidoc.subtitle', 'Nạp tài liệu nội bộ để AI tra cứu và trả lời theo đúng quy định của công ty'));

    let selectedFile = null;
    let courseList = [];
    // Danh sách câu trả lời cài sẵn đang hiện — giữ lại để nút Sửa điền sẵn form mà không phải gọi lại máy chủ.
    // PHẢI khai báo ở đây chứ không phải cạnh loadQuickReply() bên dưới: let nằm giữa hàm thì lúc lượt nạp
    // đầu tiên chạy (ngay đầu hàm này) nó vẫn trong "vùng chết" và cả bảng chỉ hiện ra một dòng báo lỗi.
    let quickReplyList = [];
    // Cùng lý do: danh sách câu đã ghi nhớ đang hiện, để nút Sửa điền sẵn form.
    let memoryList = [];
    let memorySearchTimer = null;
    let isReady = false;
    let askController = null;   // dùng để hủy câu trả lời đang sinh dở

    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('f-file');
    const btnUpload = document.getElementById('btn-upload');
    const btnClear = document.getElementById('btn-clear');

    attachDropZone(dropZone, fileInput, pickFile);   // js/app.js

    btnClear.addEventListener('click', () => pickFile(null));
    btnUpload.addEventListener('click', upload);
    document.getElementById('btn-ask').addEventListener('click', ask);
    document.getElementById('btn-stop').addEventListener('click', () => askController?.abort());
    document.getElementById('f-query').addEventListener('keydown', (e) => { if (e.key === 'Enter') ask(); });

    document.getElementById('btn-sync').addEventListener('click', () => syncKnowledge(false));
    document.getElementById('btn-sync-force').addEventListener('click', () => syncKnowledge(true));

    document.getElementById('btn-qr-add').addEventListener('click', () => openQuickReplyForm(null));
    document.getElementById('btn-qr-save').addEventListener('click', saveQuickReply);
    document.getElementById('btn-qr-test').addEventListener('click', testQuickReply);
    document.getElementById('f-qr-test').addEventListener('keydown', (e) => { if (e.key === 'Enter') testQuickReply(); });

    document.getElementById('btn-mem-save').addEventListener('click', saveMemory);
    document.getElementById('btn-mem-clear').addEventListener('click', clearMemory);
    document.getElementById('f-mem-search').addEventListener('input', () => {
        clearTimeout(memorySearchTimer);
        memorySearchTimer = setTimeout(loadMemory, 300);
    });

    await loadStatus();
    await loadCourseList();
    await loadDocumentList();
    await loadKnowledge();
    await loadQuickReply();
    await loadMemory();

    // ==================== TRẠNG THÁI MÁY CHỦ AI ====================
    async function loadStatus() {
        const card = document.getElementById('status-card');
        try {
            const s = await API.get('/aidocument/status');
            isReady = s.isReady;
            card.innerHTML = `
                <div class="card-body" style="display:flex; gap:12px; align-items:center; flex-wrap:wrap">
                    <span class="badge ${s.isReady ? 'green' : 'red'}">
                        <i class="ph ${s.isReady ? 'ph-check-circle' : 'ph-warning-circle'}"></i>
                        ${s.isReady ? t('aidoc.sanSang', 'Sẵn sàng') : t('aidoc.chuaSanSang', 'Chưa sẵn sàng')}
                    </span>
                    <span>${esc(s.message)}</span>
                </div>`;
        } catch (ex) {
            isReady = false;
            card.innerHTML = `<div class="card-body"><span class="badge red">${esc(ex.message)}</span></div>`;
        }
        refreshUploadButton();
    }

    // ==================== DANH SÁCH KHÓA HỌC ====================
    async function loadCourseList() {
        const select = document.getElementById('f-course');
        try {
            const paged = await API.get('/course?pageSize=200');
            courseList = paged.item || [];
        } catch { courseList = []; }

        select.innerHTML = `<option value="">${t('aidoc.dungChung', '— Dùng chung toàn công ty —')}</option>`
            + courseList.map(c => `<option value="${c.id}">${esc(c.title)}</option>`).join('');
    }

    // ==================== CHỌN FILE ====================
    function pickFile(file) {
        selectedFile = file || null;
        fileInput.value = '';

        const label = document.getElementById('drop-label');
        dropZone.classList.toggle('has-file', !!selectedFile);
        btnClear.hidden = !selectedFile;

        label.innerHTML = selectedFile
            ? `<b>${esc(selectedFile.name)}</b> · ${(selectedFile.size / 1024 / 1024).toFixed(2)} MB`
            : t('aidoc.keoThaFile', 'Kéo thả file vào đây hoặc bấm để chọn');

        document.getElementById('upload-note').textContent = '';
        refreshUploadButton();
    }

    function refreshUploadButton() {
        btnUpload.disabled = !selectedFile || !isReady;
    }

    // ==================== NẠP TÀI LIỆU ====================
    async function upload() {
        if (!selectedFile) return;

        const courseId = document.getElementById('f-course').value;
        const path = `/aidocument/upload${courseId ? `?courseId=${encodeURIComponent(courseId)}` : ''}`;
        const note = document.getElementById('upload-note');
        const originalLabel = btnUpload.innerHTML;

        btnUpload.disabled = true;
        btnClear.hidden = true;

        try {
            const result = await API.uploadWithProgress(path, selectedFile, (percent) => {
                // Tải file xong không có nghĩa là nạp xong: máy chủ còn phải bóc tách và sinh vector,
                // với tài liệu dài có thể mất vài phút — nói rõ để người dùng không tưởng hệ thống treo.
                btnUpload.innerHTML = percent < 100
                    ? `<span class="spinner-inline"></span> ${t('aidoc.dangTaiLen', 'Đang tải lên')}... ${percent}%`
                    : `<span class="spinner-inline"></span> ${t('aidoc.dangPhanTich', 'Đang phân tích và sinh vector')}...`;
            });

            // Bỏ chọn file TRƯỚC khi ghi kết quả: pickFile() xóa sạch ô ghi chú, gọi sau sẽ nuốt mất
            // thông báo vừa hiện và người dùng không thấy tài liệu đã nạp bao nhiêu đoạn.
            pickFile(null);

            note.innerHTML = `<span class="badge green"><i class="ph ph-check-circle"></i> ${t('aidoc.daNap', 'Đã nạp')}</span>
                <b>${esc(result.sourceFileName)}</b> · ${result.chunkCount} ${t('aidoc.doan', 'đoạn')}
                · ${result.characterCount.toLocaleString('vi-VN')} ${t('aidoc.kyTu', 'ký tự')}
                ${result.removedOldChunkCount ? ` · ${t('aidoc.daThayThe', 'đã thay thế bản cũ')} (${result.removedOldChunkCount} ${t('aidoc.doan', 'đoạn')})` : ''}`;

            toastOk(`${t('aidoc.daNapTaiLieu', 'Đã nạp tài liệu')} "${result.sourceFileName}".`);
            await loadDocumentList();
        } catch (ex) {
            toastErr(ex.message);
            note.innerHTML = `<span class="badge red">${esc(ex.message)}</span>`;
        } finally {
            btnUpload.innerHTML = originalLabel;
            btnClear.hidden = !selectedFile;
            refreshUploadButton();
        }
    }

    // ==================== DANH SÁCH TÀI LIỆU ĐÃ NẠP ====================
    // ==================== AI HỌC NỘI DUNG KHÓA HỌC ====================
    async function loadKnowledge() {
        const box = document.getElementById('knowledge-box');
        box.innerHTML = skeletonHtml('list', { count: 3, avatar: false });

        try {
            const data = await API.get('/aidocument/course-knowledge');

            box.innerHTML = `
                <div class="action-row mb" style="gap:8px; flex-wrap:wrap">
                    <span class="badge ${data.isEmbeddingReady ? 'green' : 'red'}">
                        ${data.isEmbeddingReady
                            ? `<i class="ph ph-check-circle"></i> ${esc(t('aidoc.sanSang', 'Sẵn sàng'))}`
                            : `<i class="ph ph-warning-circle"></i> ${esc(data.message)}`}
                    </span>
                    <span class="badge blue">${data.indexedCourseCount}/${data.courseCount} ${esc(t('aidoc.khoaHocDaHoc', 'khóa học đã học'))}</span>
                    <span class="badge">${data.chunkCount} ${esc(t('aidoc.doan', 'đoạn'))}</span>
                    ${data.lastSyncedAt
                        ? `<span class="small muted">${esc(t('aidoc.lanHocGanNhat', 'Lần học gần nhất'))}: ${formatDate(data.lastSyncedAt, true)}</span>`
                        : ''}
                </div>

                <div class="table-wrap"><table>
                    <thead><tr>
                        <th data-i18n="aidoc.colKhoaHocHoc">Khóa học</th>
                        <th class="text-center" data-i18n="aidoc.colBaiHoc">Bài học đã nạp</th>
                        <th class="text-center" data-i18n="aidoc.colDoan">Số đoạn</th>
                        <th data-i18n="aidoc.colLanHoc">Lần học gần nhất</th>
                    </tr></thead>
                    <tbody>${data.itemList.length ? data.itemList.map(x => `
                        <tr>
                            <td><b>${esc(x.courseTitle)}</b></td>
                            <td class="text-center">
                                ${x.indexedLessonCount}/${x.lessonCount}
                                ${x.chunkCount === 0
                                    ? `<span class="badge red">${esc(t('aidoc.chuaHoc', 'Chưa học'))}</span>`
                                    : x.indexedLessonCount < x.lessonCount
                                        ? `<span class="badge orange">${esc(t('aidoc.hocMotPhan', 'Học một phần'))}</span>`
                                        : ''}
                            </td>
                            <td class="text-center">${x.chunkCount}</td>
                            <td class="small">${x.syncedAt ? formatDate(x.syncedAt, true) : '—'}</td>
                        </tr>`).join('')
                        : `<tr><td colspan="4">${emptyHtml(t('aidoc.chuaCoKhoaHoc', 'Chưa có khóa học nào được xuất bản hoặc phân công cho học viên.'), '<i class="ph ph-graduation-cap"></i>')}</td></tr>`}
                    </tbody>
                </table></div>`;
        } catch (ex) {
            box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        }
    }

    /** Bắt AI học lại ngay. Khóa hai nút trong lúc chạy: mỗi lượt gọi Ollama sinh vector cho hàng trăm đoạn,
     *  bấm chồng lên nhau là hai lượt cùng ghi vào một bảng và cùng chậm đi. */
    async function syncKnowledge(isForced) {
        if (isForced && !await confirmDialog(
            t('aidoc.xacNhanHocLai', 'Nạp lại toàn bộ nội dung khóa học? Việc này có thể mất vài phút.'),
            { okText: t('aidoc.hocLaiTatCa', 'Học lại tất cả') })) return;

        const btnList = [document.getElementById('btn-sync'), document.getElementById('btn-sync-force')];
        btnList.forEach(b => b.disabled = true);

        const box = document.getElementById('knowledge-box');
        box.innerHTML = `<div class="flex-center" style="gap:8px; padding:14px">
                <span class="spinner-inline"></span>
                <span>${esc(t('aidoc.dangHoc', 'HB Minerva đang đọc nội dung khóa học...'))}</span>
            </div>`;

        try {
            const result = await API.post(`/aidocument/course-knowledge/sync?isForced=${isForced}`, {});

            toastOk(result.indexedUnitCount > 0 || result.removedChunkCount > 0
                ? `${t('aidoc.daHocXong', 'Đã học xong')}: ${result.indexedUnitCount} ${t('aidoc.muc', 'mục')} `
                  + `(${result.chunkCount} ${t('aidoc.doan', 'đoạn')})`
                : t('aidoc.khongCoGiMoi', 'Nội dung khóa học không thay đổi — không cần học lại.'));
        } catch (ex) {
            toastErr(ex.message);
        } finally {
            btnList.forEach(b => b.disabled = false);
            await loadKnowledge();
        }
    }

    // ==================== CÂU TRẢ LỜI CÀI SẴN ====================
    async function loadQuickReply() {
        const box = document.getElementById('qr-list');
        box.innerHTML = skeletonHtml('list', { count: 3, avatar: false });

        try {
            quickReplyList = await API.get('/aiquickreply');

            box.innerHTML = `
                <div class="table-wrap"><table>
                    <thead><tr>
                        <th style="width:34%" data-i18n="aiqr.colTuKhoa">Từ khóa</th>
                        <th data-i18n="aiqr.colCauTraLoi">Câu trả lời</th>
                        <th class="text-center" data-i18n="aiqr.colKieuKhop">So khớp</th>
                        <th class="text-center" data-i18n="aiqr.colThuTu">Thứ tự</th>
                        <th class="text-right"></th>
                    </tr></thead>
                    <tbody>${quickReplyList.length ? quickReplyList.map(x => `
                        <tr${x.isActive ? '' : ' style="opacity:.55"'}>
                            <td>
                                <b>${esc(x.keyword.split('|')[0])}</b>
                                ${x.keyword.includes('|') ? `<span class="small muted"> +${x.keyword.split('|').length - 1}</span>` : ''}
                                ${x.isActive ? '' : `<span class="badge gray">${esc(t('aiqr.dangTat', 'Đang tắt'))}</span>`}
                                <div class="small muted" style="margin-top:3px; word-break:break-word">${esc(x.keyword.split('|').join(' · '))}</div>
                            </td>
                            <td class="small" style="white-space:pre-wrap">${esc(x.answer)}</td>
                            <td class="text-center small">${x.matchType === 'Contains'
                                ? `<span class="badge orange">${esc(t('aiqr.chua', 'Chứa'))}</span>`
                                : `<span class="badge blue">${esc(t('aiqr.caCau', 'Cả câu'))}</span>`}</td>
                            <td class="text-center">${x.orderIndex}</td>
                            <td class="text-right nowrap">
                                <button class="btn ghost sm" data-qr-edit="${x.id}">${esc(t('common.edit', 'Sửa'))}</button>
                                <button class="btn ghost sm" data-qr-del="${x.id}" style="color:var(--danger)">${esc(t('common.delete', 'Xóa'))}</button>
                            </td>
                        </tr>`).join('')
                        : `<tr><td colspan="5">${emptyHtml(t('aiqr.chuaCo', 'Chưa cài câu trả lời nào.'), '<i class="ph ph-lightning"></i>')}</td></tr>`}
                    </tbody>
                </table></div>`;

            box.querySelectorAll('[data-qr-edit]').forEach(b =>
                b.addEventListener('click', () => openQuickReplyForm(Number(b.dataset.qrEdit))));
            box.querySelectorAll('[data-qr-del]').forEach(b =>
                b.addEventListener('click', () => deleteQuickReply(Number(b.dataset.qrDel))));
        } catch (ex) {
            box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        }
    }

    /** Mở form thêm mới (id rỗng) hoặc sửa một dòng đã có. */
    function openQuickReplyForm(id) {
        const item = id ? quickReplyList.find(x => x.id === id) : null;

        document.getElementById('qr-modal-title').textContent = item
            ? t('aiqr.sua', 'Sửa câu trả lời cài sẵn')
            : t('aiqr.them', 'Thêm câu trả lời');

        document.getElementById('f-qr-id').value = item?.id ?? '';
        // Hiện mỗi từ khóa một dòng. Trong database chúng nối bằng dấu "|", nhưng một chuỗi dài toàn dấu
        // gạch đứng thì đọc bằng mắt gần như không ra chữ nào — mà đây đúng là ô người ta phải đọc kỹ nhất.
        document.getElementById('f-qr-keyword').value = (item?.keyword ?? '').split('|').join('\n');
        document.getElementById('f-qr-answer').value = item?.answer ?? '';
        document.getElementById('f-qr-match').value = item?.matchType ?? 'Exact';
        document.getElementById('f-qr-active').checked = item ? item.isActive : true;
        // Dòng mới xếp xuống cuối: chen vào giữa thì nó có thể chặn mất một dòng đang chạy đúng.
        document.getElementById('f-qr-order').value = item
            ? item.orderIndex
            : (quickReplyList.reduce((max, x) => Math.max(max, x.orderIndex), 0) + 1);

        openModal('modal-qr');
    }

    async function saveQuickReply() {
        const id = document.getElementById('f-qr-id').value;
        const body = {
            // Gộp lại thành dạng lưu trữ "a|b|c". Máy chủ nhận cả hai kiểu (xem QuickReplyMatcher), nhưng
            // giữ đúng MỘT dạng trong database thì danh sách bên dưới và dữ liệu cài sẵn mới hiện giống nhau.
            keyword: document.getElementById('f-qr-keyword').value
                .split(/[|\r\n]+/).map(x => x.trim()).filter(Boolean).join('|'),
            answer: document.getElementById('f-qr-answer').value.trim(),
            matchType: document.getElementById('f-qr-match').value,
            isActive: document.getElementById('f-qr-active').checked,
            orderIndex: Number(document.getElementById('f-qr-order').value) || 0
        };

        if (!body.keyword) { toastErr(t('aiqr.thieuTuKhoa', 'Bạn chưa nhập từ khóa kích hoạt.')); return; }
        if (!body.answer) { toastErr(t('aiqr.thieuCauTraLoi', 'Bạn chưa nhập câu trả lời.')); return; }

        try {
            if (id) await API.put(`/aiquickreply/${id}`, body);
            else await API.post('/aiquickreply', body);

            closeModal('modal-qr');
            toastOk(t('aiqr.daLuu', 'Đã lưu câu trả lời cài sẵn.'));
            await loadQuickReply();
        } catch (ex) {
            toastErr(ex.message);
        }
    }

    async function deleteQuickReply(id) {
        const item = quickReplyList.find(x => x.id === id);
        if (!await confirmDialog(
            `${t('aiqr.xacNhanXoa', 'Xóa câu trả lời cài sẵn này?')}\n\n${item?.keyword ?? ''}`,
            { danger: true, okText: t('common.delete', 'Xóa') })) return;

        try {
            const result = await API.del(`/aiquickreply/${id}`);
            toastOk(result.message);
            await loadQuickReply();
        } catch (ex) {
            toastErr(ex.message);
        }
    }

    /** Thử một câu hỏi mẫu. Gọi máy chủ chứ không tự so khớp ở trình duyệt: bản so khớp thật nằm ở máy chủ,
     *  viết thêm một bản ở đây là sớm muộn hai bên lệch nhau và ô thử này báo sai. */
    async function testQuickReply() {
        const question = document.getElementById('f-qr-test').value.trim();
        const box = document.getElementById('qr-test-result');

        if (!question) { toastErr(t('aiqr.nhapCauThu', 'Bạn chưa nhập câu hỏi để thử.')); return; }

        box.innerHTML = `<span class="spinner-inline"></span>`;
        try {
            const result = await API.post('/aiquickreply/test', { question });

            box.innerHTML = result.isMatched
                ? `<div class="src-item" style="border:1px solid var(--border); border-radius:10px; padding:10px 12px">
                       <span class="badge green"><i class="ph ph-check-circle"></i> ${esc(t('aiqr.coKhop', 'Khớp câu cài sẵn'))}</span>
                       <span class="small muted"> ${esc(result.keyword)}</span>
                       <div style="margin-top:8px; white-space:pre-wrap">${esc(result.answer)}</div>
                   </div>`
                : `<span class="badge gray"><i class="ph ph-info"></i> ${esc(t('aiqr.khongKhop',
                       'Không khớp câu cài sẵn nào — câu này sẽ được AI tra tài liệu và tự trả lời.'))}</span>`;
        } catch (ex) {
            box.innerHTML = `<span class="badge red">${esc(ex.message)}</span>`;
        }
    }

    // ==================== CÂU TRẢ LỜI ĐÃ GHI NHỚ ====================
    async function loadMemory() {
        const box = document.getElementById('mem-list');
        if (!memoryList.length) box.innerHTML = skeletonHtml('list', { count: 3, avatar: false });

        try {
            const keyword = document.getElementById('f-mem-search').value.trim();
            const data = await API.get(`/aianswermemory${keyword ? `?keyword=${encodeURIComponent(keyword)}` : ''}`);
            memoryList = data.itemList;

            box.innerHTML = `
                <div class="flex-center mb" style="gap:8px; justify-content:flex-start; flex-wrap:wrap">
                    <span class="badge blue">${data.totalCount} ${esc(t('aimem.cauDaNho', 'câu đã ghi nhớ'))}</span>
                    <span class="badge green"><i class="ph ph-lightning"></i> ${data.totalHitCount} ${esc(t('aimem.lanTraLoiNgay', 'lần trả lời ngay'))}</span>
                    ${data.totalCount > memoryList.length
                        ? `<span class="small muted">${esc(t('aimem.dangHien', 'Đang hiện'))} ${memoryList.length} ${esc(t('aimem.cauDungNhieuNhat', 'câu được dùng lại nhiều nhất'))}</span>`
                        : ''}
                </div>
                <div class="table-wrap"><table>
                    <thead><tr>
                        <th style="width:30%" data-i18n="aimem.cauHoi">Câu hỏi</th>
                        <th data-i18n="aimem.cauTraLoi">Câu trả lời</th>
                        <th class="text-center" data-i18n="aimem.colDungLai">Dùng lại</th>
                        <th data-i18n="aimem.colGanNhat">Gần nhất</th>
                        <th class="text-right"></th>
                    </tr></thead>
                    <tbody>${memoryList.length ? memoryList.map(x => `
                        <tr>
                            <td>
                                <b>${esc(x.question)}</b>
                                ${x.isEdited ? `<span class="badge orange">${esc(t('aimem.daSua', 'Đã sửa'))}</span>` : ''}
                            </td>
                            <td class="small" style="white-space:pre-wrap; display:-webkit-box; -webkit-line-clamp:4; -webkit-box-orient:vertical; overflow:hidden">${esc(x.answer)}</td>
                            <td class="text-center"><b>${x.hitCount}</b></td>
                            <td class="small nowrap">${formatDate(x.lastUsedAt, true)}</td>
                            <td class="text-right nowrap">
                                <button class="btn ghost sm" data-mem-edit="${x.id}">${esc(t('common.edit', 'Sửa'))}</button>
                                <button class="btn ghost sm" data-mem-del="${x.id}" style="color:var(--danger)">${esc(t('common.delete', 'Xóa'))}</button>
                            </td>
                        </tr>`).join('')
                        : `<tr><td colspan="5">${emptyHtml(keyword
                            ? t('aimem.khongTimThay', 'Không có câu nào khớp từ khóa này.')
                            : t('aimem.chuaCo', 'Chưa ghi nhớ câu nào. Khi học viên hỏi HB Minerva, câu trả lời sẽ được ghi nhớ ở đây.'),
                            '<i class="ph ph-brain"></i>')}</td></tr>`}
                    </tbody>
                </table></div>`;

            box.querySelectorAll('[data-mem-edit]').forEach(b =>
                b.addEventListener('click', () => openMemoryForm(Number(b.dataset.memEdit))));
            box.querySelectorAll('[data-mem-del]').forEach(b =>
                b.addEventListener('click', () => deleteMemory(Number(b.dataset.memDel))));
        } catch (ex) {
            box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        }
    }

    function openMemoryForm(id) {
        const item = memoryList.find(x => x.id === id);
        if (!item) return;

        document.getElementById('f-mem-id').value = item.id;
        document.getElementById('f-mem-question').textContent = item.question;
        document.getElementById('f-mem-answer').value = item.answer;
        openModal('modal-mem');
    }

    async function saveMemory() {
        const id = document.getElementById('f-mem-id').value;
        const answer = document.getElementById('f-mem-answer').value.trim();
        if (!answer) { toastErr(t('aimem.thieuCauTraLoi', 'Câu trả lời không được để trống. Muốn AI quên câu này thì bấm Xóa.')); return; }

        try {
            const result = await API.put(`/aianswermemory/${id}`, { answer });
            closeModal('modal-mem');
            toastOk(result.message);
            await loadMemory();
        } catch (ex) {
            toastErr(ex.message);
        }
    }

    async function deleteMemory(id) {
        const item = memoryList.find(x => x.id === id);
        if (!await confirmDialog(
            `${t('aimem.xacNhanXoa', 'Xóa câu trả lời đã ghi nhớ này? Lần tới có người hỏi, AI sẽ trả lời lại từ đầu.')}\n\n${item?.question ?? ''}`,
            { danger: true, okText: t('common.delete', 'Xóa') })) return;

        try {
            const result = await API.del(`/aianswermemory/${id}`);
            toastOk(result.message);
            await loadMemory();
        } catch (ex) {
            toastErr(ex.message);
        }
    }

    async function clearMemory() {
        if (!await confirmDialog(
            t('aimem.xacNhanXoaTatCa', 'Xóa TẤT CẢ câu trả lời đã ghi nhớ? Dùng khi vừa thay hàng loạt quy định — mọi câu hỏi sau đó AI sẽ trả lời lại từ đầu.'),
            { danger: true, okText: t('aimem.xoaTatCa', 'Xóa tất cả') })) return;

        try {
            const result = await API.del('/aianswermemory');
            toastOk(result.message);
            await loadMemory();
        } catch (ex) {
            toastErr(ex.message);
        }
    }

    async function loadDocumentList() {
        const box = document.getElementById('doc-list');
        box.innerHTML = skeletonHtml('list', { count: 4, avatar: false });

        try {
            const list = await API.get('/aidocument');
            const totalChunk = list.reduce((sum, x) => sum + x.chunkCount, 0);

            box.innerHTML = `
                <div class="card">
                    <div class="card-head">
                        <h3>${list.length} ${t('aidoc.taiLieu', 'tài liệu')} · ${totalChunk} ${t('aidoc.doan', 'đoạn')}</h3>
                    </div>
                    <div class="table-wrap"><table>
                        <thead><tr>
                            <th data-i18n="aidoc.colTen">Tài liệu</th>
                            <th data-i18n="aidoc.colKhoaHoc">Phạm vi</th>
                            <th class="text-center" data-i18n="aidoc.colDoan">Số đoạn</th>
                            <th data-i18n="aidoc.colModel">Model</th>
                            <th data-i18n="aidoc.colNgay">Ngày nạp</th>
                            <th class="text-right"></th>
                        </tr></thead>
                        <tbody>${list.length ? list.map(x => `
                            <tr>
                                <td><b>${esc(x.sourceFileName)}</b>
                                    ${x.sourceFileUrl ? ` <a href="${esc(x.sourceFileUrl)}" target="_blank" class="small" title="${t('aidoc.moFileGoc', 'Mở file gốc')}"><i class="ph ph-arrow-square-out"></i></a>` : ''}
                                </td>
                                <td class="small">${x.courseTitle
                                    ? esc(x.courseTitle)
                                    : `<span class="badge blue">${t('aidoc.toanCongTy', 'Toàn công ty')}</span>`}</td>
                                <td class="text-center">${x.chunkCount}</td>
                                <td class="small muted">${esc(x.embeddingModel)}</td>
                                <td class="small">${formatDate(x.createdAt, true)}</td>
                                <td class="text-right nowrap">
                                    <button class="btn ghost sm" data-view="${esc(x.sourceFileName)}" data-course="${x.courseId ?? ''}">${t('aidoc.xemDoan', 'Xem đoạn')}</button>
                                    <button class="btn ghost sm" data-del="${esc(x.sourceFileName)}" data-course="${x.courseId ?? ''}" style="background-color: red; color: white">${t('common.delete', 'Xóa')}</button>
                                </td>
                            </tr>`).join('')
                            : `<tr><td colspan="6">${emptyHtml(t('aidoc.chuaCoTaiLieu', 'Chưa nạp tài liệu nào.'), '<i class="ph ph-file-text"></i>')}</td></tr>`}
                        </tbody>
                    </table></div>
                </div>`;

            box.querySelectorAll('[data-view]').forEach(b =>
                b.addEventListener('click', () => viewChunkList(b.dataset.view, b.dataset.course)));

            box.querySelectorAll('[data-del]').forEach(b =>
                b.addEventListener('click', () => remove(b.dataset.del, b.dataset.course)));
        } catch (ex) {
            box.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        }
    }

    async function viewChunkList(sourceFileName, courseId) {
        const body = document.getElementById('chunk-body');
        document.getElementById('chunk-title').textContent = sourceFileName;
        body.innerHTML = skeletonHtml('text', { lines: 6 });
        openModal('modal-chunk');

        try {
            const list = await API.get(`/aidocument/chunk?sourceFileName=${encodeURIComponent(sourceFileName)}`
                + (courseId ? `&courseId=${encodeURIComponent(courseId)}` : ''));

            body.innerHTML = list.length
                ? `<div class="small muted mb">${t('aidoc.giaiThichDoan',
                        'Mỗi đoạn dưới đây là một đơn vị AI dùng để tra cứu. Hai đoạn liền nhau cố ý lặp phần đầu/cuối để câu bị cắt ngang vẫn còn đủ ngữ cảnh.')}</div>`
                    + list.map(c => `<div class="chunk-item"><div class="idx">#${c.chunkIndex + 1}</div>${esc(c.textContent)}</div>`).join('')
                : emptyHtml(t('aidoc.khongCoDoan', 'Tài liệu này không còn đoạn nào.'));
        } catch (ex) {
            body.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
        }
    }

    async function remove(sourceFileName, courseId) {
        if (!await confirmDialog(
            `${t('aidoc.xacNhanXoa', 'Xóa tài liệu')} "${sourceFileName}" ${t('aidoc.khoiKhoTriThuc', 'khỏi kho tri thức?')}`,
            { danger: true, okText: t('common.delete', 'Xóa') })) return;

        try {
            const r = await API.del(`/aidocument?sourceFileName=${encodeURIComponent(sourceFileName)}`
                + (courseId ? `&courseId=${encodeURIComponent(courseId)}` : ''));
            toastOk(r.message);
            await loadDocumentList();
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== HỎI ĐÁP TRÊN TÀI LIỆU ====================
    async function ask() {
        const question = document.getElementById('f-query').value.trim();
        const answerBox = document.getElementById('answer-box');
        const sourceBox = document.getElementById('source-box');
        const timerBox = document.getElementById('ask-timer');
        const btnAsk = document.getElementById('btn-ask');
        const btnStop = document.getElementById('btn-stop');

        if (!question) { toastErr(t('aidoc.nhapCauHoi', 'Vui lòng nhập câu hỏi cần tìm.')); return; }
        if (askController) return;   // đang trả lời dở thì không nhận câu mới

        const courseId = document.getElementById('f-course').value;
        askController = new AbortController();
        btnAsk.disabled = true;
        btnStop.hidden = false;
        sourceBox.innerHTML = '';
        
        // Thêm class 'ai-thinking' để tạo hiệu ứng viền phát sáng khi AI đang nghĩ
        answerBox.className = 'mt-xl ai-thinking';
        answerBox.innerHTML = `
            <div class="ai-answer">
                <span class="spinner-inline"></span> 
                <span style="color: var(--text-muted)">${t('aidoc.dangDocTaiLieu', 'AI đang đọc tài liệu và phân tích...')}</span>
            </div>`;

        const startedAt = Date.now();
        const timer = setInterval(() => {
            timerBox.textContent = `${Math.round((Date.now() - startedAt) / 1000)}s`;
        }, 1000);

        let answer = '';
        let sourceList = [];

        function renderAnswer(pending) {
            // Số trích dẫn [1], [2] do model chèn — tô lên cho dễ đối chiếu với danh sách nguồn bên dưới.
            // Bảng, chữ đậm, gạch đầu dòng: aiAnswerHtml trong app.js, dùng chung với trang HB Minerva.
            const html = aiAnswerHtml(esc(answer)
                .replace(/\[(\d{1,2})\]/g, '<span class="badge" style="background:#a855f7; color:#fff; padding:0 6px; border-radius: 4px; font-size: 12px; vertical-align: super;">$1</span>'));
            
            // Dùng class ai-answer thay vì chunk-item để UI đẹp hơn
            answerBox.innerHTML = `<div class="ai-answer">${html}${pending ? '<span class="spinner-inline" style="margin-left:6px; color:#a855f7"></span>' : ''}</div>`;
        }

        function renderSource() {
            if (!sourceList.length) return;
            sourceBox.innerHTML = `
                <details>
                    <summary class="small muted" style="cursor:pointer">
                        ${t('aidoc.nguonThamKhao', 'Nguồn tham khảo')} (${sourceList.length}) — ${t('aidoc.bamDeXem', 'bấm để xem đoạn tài liệu AI đã đọc')}
                    </summary>
                    <div style="margin-top:10px">
                        ${sourceList.map(x => `
                            <div class="chunk-item">
                                <div style="display:flex; gap:10px; align-items:center; margin-bottom:6px">
                                    <span class="badge blue" style="padding:0 6px">${x.index}</span>
                                    <span class="idx">${esc(x.sourceFileName)} · #${x.chunkIndex + 1}</span>
                                    ${x.score > 0 ? `<span class="score-bar" style="flex:1"><span style="width:${Math.max(0, Math.min(100, x.score * 100)).toFixed(0)}%"></span></span>
                                        <b class="small">${Math.min(100, x.score * 100).toFixed(1)}%</b>` : ''}
                                </div>
                                ${esc(x.textContent)}
                            </div>`).join('')}
                    </div>
                </details>`;
        }

        try {
            const response = await fetch('/api/aidocument/ask', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${Auth.token}` },
                body: JSON.stringify({ question, courseId: courseId ? +courseId : null }),
                signal: askController.signal
            });

            if (response.status === 401) { Auth.clear(); location.href = '/pages/login/login.html'; return; }
            if (!response.ok) throw new Error(`${t('aidoc.loiMayChu', 'Máy chủ trả về lỗi')} ${response.status}`);

            const reader = response.body.getReader();
            const decoder = new TextDecoder('utf-8');
            let buffer = '';

            while (true) {
                const { value, done } = await reader.read();
                if (done) break;

                buffer += decoder.decode(value, { stream: true });

                // Mỗi sự kiện SSE kết thúc bằng một dòng trống; phần đuôi chưa đủ thì giữ lại chờ gói sau.
                let cut;
                while ((cut = buffer.indexOf('\n\n')) >= 0) {
                    const raw = buffer.slice(0, cut);
                    buffer = buffer.slice(cut + 2);

                    const name = raw.match(/^event: (.+)$/m)?.[1];
                    const dataText = raw.match(/^data: ([\s\S]*)$/m)?.[1];
                    if (!name || !dataText) continue;

                    let data;
                    try { data = JSON.parse(dataText); } catch { continue; }

                    if (name === 'sources') { sourceList = data; renderSource(); }
                    else if (name === 'token') { answer += data.t; renderAnswer(true); }
                    else if (name === 'error') { throw new Error(data.message); }
                }
            }

            renderAnswer(false);
            if (!answer.trim()) answerBox.innerHTML = emptyHtml(
                t('aidoc.khongTraLoiDuoc', 'AI không trả lời được. Hãy kiểm tra model sinh câu trả lời đã được cài chưa.'),
                '<i class="ph ph-warning"></i>');
        } catch (ex) {
            if (ex.name === 'AbortError') {
                // Người dùng bấm Dừng: giữ lại phần đã sinh được thay vì xóa trắng.
                renderAnswer(false);
                if (!answer.trim()) answerBox.innerHTML = `<div class="small muted">${t('aidoc.daDung', 'Đã dừng.')}</div>`;
            } else {
                answerBox.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning"></i>');
                toastErr(ex.message);
            }
        } finally {
            clearInterval(timer);
            askController = null;
            btnAsk.disabled = false;
            btnStop.hidden = true;
            answerBox.classList.remove('ai-thinking'); // Dừng hiệu ứng phát sáng
            if(!answer.trim() && !answerBox.innerHTML.includes('ph-warning')) {
                timerBox.textContent = '';
            }
        }
    }

})();
