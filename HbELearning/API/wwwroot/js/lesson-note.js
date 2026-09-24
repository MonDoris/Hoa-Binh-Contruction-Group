// File: wwwroot/js/lesson-note.js
// ============================================================================================
// SỔ GHI CHÚ THEO MỐC THỜI GIAN CỦA VIDEO BÀI HỌC
//
// Học viên đang xem tới phút 2:15 thấy một ý đáng nhớ thì bấm "Ghi chú tại đây": ô soạn mở ra kèm sẵn mốc
// 2:15, gõ xong lưu lại. Về sau mở lại bài, bấm vào ghi chú đó là video nhảy thẳng về 2:15.
//
// Vì sao là file riêng chứ không nhét vào video-learning.js: trình phát lo việc PHÁT (chặn tua, câu hỏi
// giữa chừng, chương mục) — đó đã là một khối đủ nặng. Ghi chú chỉ cần hai thứ từ nó: giây đang xem và một
// đường tua tới giây bất kỳ; cả hai đều nằm trong đối tượng mà mountVideoLearning trả về. Tách ra thì trang
// nào không cần ghi chú (VD trang xem thử của người soạn bài) chỉ việc không gọi tới đây.
//
// Ghi chú là SỔ RIÊNG của từng người: API chỉ trả về ghi chú của chính người đang đăng nhập và từ chối mọi
// truy cập chéo (xem Controllers/LessonNoteController.cs).
// ============================================================================================

/**
 * Dựng sổ ghi chú vào một phần tử DOM.
 *
 * @param {object} options
 * @param {HTMLElement} options.container    Nơi vẽ sổ ghi chú.
 * @param {number}      options.enrollmentId
 * @param {number}      options.lessonId
 * @param {object}      options.player       Đối tượng do mountVideoLearning() trả về — cần currentSecond
 *                                           để biết đang xem tới đâu và jumpToSecond() để bấm về đoạn đã ghi.
 * @returns {{ compose: Function, reload: Function, destroy: Function }}
 */
function mountLessonNote(options) {
    const { container, enrollmentId, lessonId, player } = options;

    let noteList = [];
    let draftSecond = null;   // giây của ghi chú đang soạn; null = ô soạn đang đóng
    let editingId = null;     // id ghi chú đang sửa; null = đang viết ghi chú mới
    let destroyed = false;

    /** Giây -> "m:ss" hoặc "h:mm:ss", khớp cách trình phát hiển thị thời gian. */
    function formatClock(totalSecond) {
        const s = Math.max(0, Math.floor(totalSecond || 0));
        const hour = Math.floor(s / 3600);
        const minute = Math.floor((s % 3600) / 60);
        const second = s % 60;
        const pad = (n) => String(n).padStart(2, '0');
        return hour > 0 ? `${hour}:${pad(minute)}:${pad(second)}` : `${minute}:${pad(second)}`;
    }

    // ---------- Vẽ ----------

    function render() {
        if (destroyed || !container) return;

        container.innerHTML = `
            <div class="note-panel">
                <div class="note-head">
                    <b><i class="ph ph-note-pencil"></i> ${esc(t('note.tieuDe', 'Ghi chú của tôi'))}</b>
                    <span class="note-count">${noteList.length}</span>
                    <button type="button" class="btn ghost sm note-add">
                        <i class="ph ph-plus"></i> ${esc(t('note.themTaiDay', 'Ghi chú tại đây'))}
                    </button>
                </div>
                ${draftSecond === null ? '' : composerHtml()}
                ${listHtml()}
            </div>`;

        bind();

        // Đặt con trỏ vào ô soạn ngay khi nó vừa mở: học viên bấm "Ghi chú tại đây" là để GÕ, bắt họ bấm
        // thêm một nhát vào ô nữa là thừa một thao tác ở đúng lúc họ đang vội ghi lại ý vừa nghĩ ra.
        const box = container.querySelector('.note-input');
        if (box) {
            box.focus();
            box.setSelectionRange(box.value.length, box.value.length);
        }
    }

    function composerHtml() {
        const note = editingId ? noteList.find(x => x.id === editingId) : null;
        return `
            <div class="note-composer">
                <div class="note-composer-head">
                    <span class="note-time">${esc(formatClock(draftSecond))}</span>
                    <span class="small muted">${esc(editingId
                        ? t('note.dangSua', 'Đang sửa ghi chú')
                        : t('note.tuMocNay', 'Ghi chú cho đoạn này của video'))}</span>
                </div>
                <textarea class="note-input" rows="3" maxlength="2000"
                          placeholder="${esc(t('note.goiY', 'Điều bạn muốn nhớ lại ở đoạn này...'))}">${esc(note?.content || '')}</textarea>
                <div class="note-composer-foot">
                    <span class="small muted note-hint">${esc(t('note.phimTat', 'Ctrl + Enter để lưu'))}</span>
                    <button type="button" class="btn ghost sm note-cancel">${esc(t('common.cancel', 'Hủy'))}</button>
                    <button type="button" class="btn sm note-save">${esc(t('common.save', 'Lưu'))}</button>
                </div>
            </div>`;
    }

    function listHtml() {
        if (!noteList.length)
            return `<div class="note-empty">${esc(t('note.chuaCo',
                'Chưa có ghi chú nào. Đang xem tới đoạn cần nhớ thì bấm "Ghi chú tại đây".'))}</div>`;

        return `<div class="note-list">${noteList.map(note => `
            <div class="note-item" data-note="${note.id}">
                <button type="button" class="note-jump" data-jump="${note.atSecond}"
                        title="${esc(t('note.bamDeXemLai', 'Bấm để xem lại đoạn này'))}">
                    <i class="ph ph-play-circle"></i> ${esc(formatClock(note.atSecond))}
                </button>
                <div class="note-body">
                    <div class="note-text">${esc(note.content)}</div>
                    <div class="note-meta">
                        ${esc(formatDateTime(note.createdAt))}
                        ${note.updatedAt ? ` · ${esc(t('note.daSua', 'đã sửa'))}` : ''}
                    </div>
                </div>
                <div class="note-actions">
                    <button type="button" class="note-icon" data-edit="${note.id}"
                            title="${esc(t('common.edit', 'Sửa'))}"><i class="ph ph-pencil-simple"></i></button>
                    <button type="button" class="note-icon" data-del="${note.id}"
                            title="${esc(t('common.delete', 'Xóa'))}"><i class="ph ph-trash"></i></button>
                </div>
            </div>`).join('')}</div>`;
    }

    /** Ngày giờ ngắn gọn. Dùng hàm dùng chung của app.js nếu có, còn không thì tự lo — file này phải chạy
     *  được cả ở trang không nạp app.js. */
    function formatDateTime(value) {
        if (typeof formatDate === 'function') return formatDate(value, true);
        const d = new Date(value);
        return isNaN(d) ? '' : d.toLocaleString();
    }

    // ---------- Sự kiện ----------

    function bind() {
        container.querySelector('.note-add')?.addEventListener('click', () => compose());

        container.querySelectorAll('[data-jump]').forEach(button =>
            button.addEventListener('click', () => player?.jumpToSecond?.(+button.dataset.jump)));

        container.querySelectorAll('[data-edit]').forEach(button =>
            button.addEventListener('click', () => {
                const note = noteList.find(x => x.id === +button.dataset.edit);
                if (!note) return;
                editingId = note.id;
                draftSecond = note.atSecond;
                render();
            }));

        container.querySelectorAll('[data-del]').forEach(button =>
            button.addEventListener('click', () => remove(+button.dataset.del)));

        container.querySelector('.note-cancel')?.addEventListener('click', closeComposer);
        container.querySelector('.note-save')?.addEventListener('click', save);

        // Ctrl/Cmd + Enter để lưu: hai tay đang trên bàn phím gõ ghi chú, với chuột ra bấm nút là chậm hơn
        // hẳn. Enter trơn cố ý KHÔNG lưu — ghi chú nhiều dòng là chuyện bình thường.
        container.querySelector('.note-input')?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); save(); }
            if (e.key === 'Escape') { e.preventDefault(); closeComposer(); }
        });
    }

    function closeComposer() {
        draftSecond = null;
        editingId = null;
        render();
    }

    /**
     * Mở ô soạn cho một mốc thời gian.
     * @param {number} [second] Bỏ trống thì lấy giây video đang dừng — dùng khi bấm nút trong chính sổ này.
     */
    function compose(second) {
        editingId = null;
        draftSecond = Math.max(0, Math.floor(second ?? player?.currentSecond ?? 0));
        render();

        // Kéo sổ ghi chú vào tầm nhìn: nút "Ghi chú tại đây" nằm trên thanh điều khiển video, mà sổ thì ở
        // dưới khung hình — bấm xong mà màn hình không nhúc nhích thì trông như nút không ăn.
        container.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    async function save() {
        const box = container.querySelector('.note-input');
        const content = (box?.value || '').trim();
        if (!content) { toastErr(t('note.chuaNhap', 'Bạn chưa nhập nội dung ghi chú.')); box?.focus(); return; }

        try {
            if (editingId) {
                const updated = await API.put(`/lessonnote/${editingId}`, { content });
                const index = noteList.findIndex(x => x.id === editingId);
                if (index >= 0) noteList[index] = updated;
            } else {
                noteList.push(await API.post('/lessonnote', {
                    enrollmentId, lessonId, atSecond: draftSecond, content
                }));
                // Sắp lại theo mốc thời gian thay vì nối vào cuối: sổ đọc từ trên xuống phải đi dọc video từ
                // đầu tới cuối, kể cả khi học viên tua ngược lại ghi bổ sung cho đoạn đã xem.
                noteList.sort((a, b) => a.atSecond - b.atSecond || a.id - b.id);
            }
            toastOk(t('note.daLuu', 'Đã lưu ghi chú.'));
            closeComposer();
        } catch (ex) { toastErr(ex.message); }
    }

    async function remove(id) {
        const note = noteList.find(x => x.id === id);
        if (!note) return;
        const preview = note.content.length > 120 ? note.content.slice(0, 120) + '…' : note.content;
        if (!await confirmDialog(`${formatClock(note.atSecond)} — ${preview}`,
            { title: t('note.xoaHoi', 'Xóa ghi chú này?'), okText: t('common.delete', 'Xóa'), danger: true })) return;

        try {
            await API.del(`/lessonnote/${id}`);
            noteList = noteList.filter(x => x.id !== id);
            if (editingId === id) { editingId = null; draftSecond = null; }
            toastOk(t('note.daXoa', 'Đã xóa ghi chú.'));
            render();
        } catch (ex) { toastErr(ex.message); }
    }

    async function reload() {
        try {
            noteList = await API.getSilent(`/lessonnote?enrollmentId=${enrollmentId}&lessonId=${lessonId}`) || [];
        } catch {
            // Sổ ghi chú hỏng không được phép chặn việc học: hiện sổ rỗng còn hơn ném lỗi đỏ lên giữa bài.
            noteList = [];
        }
        render();
    }

    reload();

    return {
        compose,
        reload,
        destroy() { destroyed = true; if (container) container.innerHTML = ''; },
    };
}
