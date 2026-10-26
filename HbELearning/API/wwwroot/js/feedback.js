// File: wwwroot/js/feedback.js
// ============================================================================================
// FEEDBACK KHÓA HỌC: bình luận khi gặp vấn đề + đánh giá sao sau khi hoàn thành khóa
//
// Một widget dùng ở hai nơi: hộp thoại "Phản hồi" trên trang Khóa học của tôi (người học) và thẻ Feedback
// ở trang chi tiết khóa học (người soạn / quản lý). Widget không tự đoán quyền theo vai trò — máy chủ trả
// kèm các cờ canComment / canReview / isOwner / isStaff (xem FeedbackService) và giao diện chỉ vẽ theo cờ đó.
// ============================================================================================

/**
 * @param {object} options
 * @param {HTMLElement} options.container
 * @param {number}      options.courseId
 * @returns {{ reload: Function }}
 */
function mountCourseFeedback(options) {
    const { container, courseId } = options;

    let data = null;
    let draftStar = 0;       // số sao đang chọn ở ô đánh giá (0 = chưa chọn)
    let replyToId = null;    // bình luận gốc đang soạn trả lời
    let editingId = null;    // bình luận / trả lời đang sửa

    const starHtml = (value, size = '') => {
        const full = Math.round(value || 0);
        return `<span class="fb-stars ${size}" aria-label="${esc(t('feedback.soSao', '{n} trên 5 sao', { n: value || 0 }))}">`
            + [1, 2, 3, 4, 5].map(i => `<i class="ph${i <= full ? '-fill' : ''} ph-star${i <= full ? ' on' : ''}"></i>`).join('')
            + '</span>';
    };

    const metaHtml = (item) => `<span class="fb-author">${esc(item.authorName)}</span>
        <span class="fb-time">${esc(formatDate(item.createdAt, true))}${item.updatedAt ? ` · ${esc(t('feedback.daSua', 'đã sửa'))}` : ''}</span>`;

    function summaryHtml() {
        const s = data.summary;
        if (!s.reviewCount)
            return `<div class="fb-summary empty"><i class="ph ph-star"></i>
                <span>${esc(t('feedback.chuaCoDanhGia', 'Chưa có đánh giá nào cho khóa học này.'))}</span></div>`;

        const rows = [5, 4, 3, 2, 1].map(star => {
            const n = s.starCountList[star - 1] || 0;
            const percent = Math.round(n * 100 / s.reviewCount);
            return `<div class="fb-bar-row"><span>${star}</span><i class="ph-fill ph-star"></i>
                <span class="fb-bar"><span style="width:${percent}%"></span></span><span class="fb-bar-n">${n}</span></div>`;
        }).join('');

        return `<div class="fb-summary">
            <div class="fb-avg"><b>${Number(s.average).toFixed(1)}</b>${starHtml(s.average, 'lg')}
                <small>${esc(t('feedback.luotDanhGia', '{n} lượt đánh giá', { n: s.reviewCount }))}</small></div>
            <div class="fb-bars">${rows}</div>
        </div>`;
    }

    function myReview() {
        return data.itemList.find(x => x.kind === 'Review' && x.isMine) || null;
    }

    function reviewFormHtml() {
        if (!data.canReview) {
            // Đang học dở: nhắc có thể đánh giá sau, chứ không để mục này biến mất khiến người học không biết có chức năng.
            return data.canComment
                ? `<p class="fb-hint"><i class="ph ph-lock-simple"></i> ${esc(t('feedback.danhGiaSauKhiXong', 'Bạn sẽ chấm sao được sau khi hoàn thành khóa học.'))}</p>`
                : '';
        }
        const mine = myReview();
        if (draftStar === 0 && mine) draftStar = mine.rating;

        const picker = [1, 2, 3, 4, 5].map(i =>
            `<button type="button" class="fb-star-btn" data-star="${i}" aria-label="${i}"><i class="ph${i <= draftStar ? '-fill' : ''} ph-star${i <= draftStar ? ' on' : ''}"></i></button>`).join('');

        return `<section class="fb-block">
            <h4>${esc(mine ? t('feedback.danhGiaCuaBan', 'Đánh giá của bạn') : t('feedback.danhGiaKhoaHoc', 'Đánh giá khóa học'))}</h4>
            <div class="fb-star-picker" role="group">${picker}</div>
            <textarea id="fb-review-text" rows="2" maxlength="2000"
                placeholder="${esc(t('feedback.nhanXetTuyChon', 'Nhận xét của bạn (không bắt buộc)'))}">${esc(mine?.content || '')}</textarea>
            <div class="fb-actions"><button type="button" class="btn sm" id="fb-review-save">
                ${esc(mine ? t('feedback.capNhatDanhGia', 'Cập nhật đánh giá') : t('feedback.guiDanhGia', 'Gửi đánh giá'))}</button></div>
        </section>`;
    }

    function commentFormHtml() {
        if (!data.canComment) return '';
        return `<section class="fb-block">
            <h4>${esc(t('feedback.binhLuanVanDe', 'Bình luận / báo vấn đề'))}</h4>
            <textarea id="fb-comment-text" rows="3" maxlength="2000"
                placeholder="${esc(t('feedback.moTaVanDe', 'Bạn gặp vấn đề gì trong khóa học? Giảng viên sẽ trả lời tại đây.'))}"></textarea>
            <div class="fb-actions"><button type="button" class="btn sm" id="fb-comment-send">
                <i class="ph ph-paper-plane-tilt"></i> ${esc(t('feedback.gui', 'Gửi'))}</button></div>
        </section>`;
    }

    function composerHtml(id, value, sendLabel) {
        return `<div class="fb-composer">
            <textarea id="${id}" rows="2" maxlength="2000">${esc(value || '')}</textarea>
            <div class="fb-actions">
                <button type="button" class="btn ghost sm" data-cancel>${esc(t('common.cancel', 'Hủy'))}</button>
                <button type="button" class="btn sm" data-submit>${esc(sendLabel)}</button>
            </div></div>`;
    }

    function itemHtml(item, parent) {
        const canEdit = item.isMine;
        const canDelete = item.isMine || data.isOwner;
        const canReply = !parent && (data.isOwner || item.isMine);

        if (editingId === item.id)
            return `<div class="fb-item" data-id="${item.id}">${composerHtml('fb-edit-text', item.content, t('common.save', 'Lưu'))}</div>`;

        return `<div class="fb-item ${parent ? 'reply' : ''}" data-id="${item.id}">
            <div class="fb-head">${metaHtml(item)}</div>
            <p class="fb-content">${esc(item.content)}</p>
            <div class="fb-tools">
                ${canReply ? `<button type="button" class="fb-link" data-reply="${item.id}">${esc(t('feedback.traLoi', 'Trả lời'))}</button>` : ''}
                ${canEdit ? `<button type="button" class="fb-link" data-edit="${item.id}">${esc(t('common.edit', 'Sửa'))}</button>` : ''}
                ${canDelete ? `<button type="button" class="fb-link danger" data-del="${item.id}">${esc(t('common.delete', 'Xóa'))}</button>` : ''}
            </div>
            ${replyToId === item.id ? `<div data-reply-box="${item.id}">${composerHtml('fb-reply-text', '', t('feedback.guiTraLoi', 'Gửi trả lời'))}</div>` : ''}
        </div>`;
    }

    function threadListHtml() {
        const commentList = data.itemList.filter(x => x.kind === 'Comment' && x.parentId == null);
        if (!commentList.length)
            return `<p class="fb-hint">${esc(t('feedback.chuaCoBinhLuan', 'Chưa có bình luận nào.'))}</p>`;

        return commentList.map(c => {
            const replies = data.itemList.filter(x => x.parentId === c.id);
            return `<div class="fb-thread">${itemHtml(c, null)}${replies.map(r => itemHtml(r, c)).join('')}</div>`;
        }).join('');
    }

    /** Người quản lý đọc được đánh giá của mọi người; người học chỉ thấy đánh giá của chính mình (ở ô đánh giá). */
    function reviewListHtml() {
        if (!data.isStaff) return '';
        const reviewList = data.itemList.filter(x => x.kind === 'Review').slice().reverse();
        if (!reviewList.length) return '';
        return `<section class="fb-block">
            <h4>${esc(t('feedback.danhGiaCuaHocVien', 'Đánh giá của học viên'))}</h4>
            ${reviewList.map(r => `<div class="fb-item" data-id="${r.id}">
                <div class="fb-head">${starHtml(r.rating)} ${metaHtml(r)}</div>
                ${r.content ? `<p class="fb-content">${esc(r.content)}</p>` : ''}
                ${data.isOwner ? `<div class="fb-tools"><button type="button" class="fb-link danger" data-del="${r.id}">${esc(t('common.delete', 'Xóa'))}</button></div>` : ''}
            </div>`).join('')}
        </section>`;
    }

    function render() {
        container.innerHTML = `<div class="fb">
            ${summaryHtml()}
            ${reviewFormHtml()}
            ${commentFormHtml()}
            <section class="fb-block">
                <h4>${esc(t('feedback.thaoLuan', 'Thảo luận'))}</h4>
                ${threadListHtml()}
            </section>
            ${reviewListHtml()}
        </div>`;
        bind();
    }

    async function run(task) {
        try { await task(); await load(); }
        catch (ex) { toastErr(ex.message); }
    }

    function bind() {
        container.querySelectorAll('[data-star]').forEach(b => b.addEventListener('click', () => {
            draftStar = +b.dataset.star;
            const keep = document.getElementById('fb-review-text')?.value ?? '';
            render();
            document.getElementById('fb-review-text').value = keep;
        }));

        document.getElementById('fb-review-save')?.addEventListener('click', () => run(async () => {
            if (!draftStar) throw new Error(t('feedback.chonSao', 'Hãy chọn số sao trước.'));
            const r = await API.put(`/feedback/course/${courseId}/review`,
                { rating: draftStar, content: document.getElementById('fb-review-text').value.trim() || null });
            toastOk(t('feedback.daLuuDanhGia', 'Đã lưu đánh giá của bạn.'));
            return r;
        }));

        document.getElementById('fb-comment-send')?.addEventListener('click', () => run(async () => {
            const content = document.getElementById('fb-comment-text').value.trim();
            if (!content) throw new Error(t('feedback.noiDungTrong', 'Nội dung đang để trống.'));
            await API.post(`/feedback/course/${courseId}/comment`, { content });
            toastOk(t('feedback.daGui', 'Đã gửi bình luận.'));
        }));

        container.querySelectorAll('[data-reply]').forEach(b => b.addEventListener('click', () => {
            replyToId = +b.dataset.reply; editingId = null; render();
            document.getElementById('fb-reply-text')?.focus();
        }));
        container.querySelectorAll('[data-edit]').forEach(b => b.addEventListener('click', () => {
            editingId = +b.dataset.edit; replyToId = null; render();
            document.getElementById('fb-edit-text')?.focus();
        }));
        container.querySelectorAll('[data-cancel]').forEach(b => b.addEventListener('click', () => {
            replyToId = null; editingId = null; render();
        }));

        container.querySelectorAll('[data-reply-box] [data-submit]').forEach(b => b.addEventListener('click', () => run(async () => {
            const content = document.getElementById('fb-reply-text').value.trim();
            if (!content) throw new Error(t('feedback.noiDungTrong', 'Nội dung đang để trống.'));
            await API.post(`/feedback/course/${courseId}/comment`,
                { content, parentId: +b.closest('[data-reply-box]').dataset.replyBox });
            replyToId = null;
        })));

        container.querySelectorAll('.fb-item [data-submit]').forEach(b => {
            if (b.closest('[data-reply-box]')) return;
            b.addEventListener('click', () => run(async () => {
                const content = document.getElementById('fb-edit-text').value.trim();
                if (!content) throw new Error(t('feedback.noiDungTrong', 'Nội dung đang để trống.'));
                await API.put(`/feedback/${editingId}`, { content });
                editingId = null;
            }));
        });

        container.querySelectorAll('[data-del]').forEach(b => b.addEventListener('click', async () => {
            if (!await confirmDialog(t('feedback.xacNhanXoa', 'Xóa phản hồi này? Các trả lời bên dưới (nếu có) cũng bị xóa.'),
                { danger: true, okText: t('common.delete', 'Xóa') })) return;
            run(async () => { await API.del(`/feedback/${b.dataset.del}`); });
        }));
    }

    async function load() {
        data = await API.get(`/feedback/course/${courseId}`);
        render();
    }

    container.innerHTML = skeletonHtml('list', { count: 3, avatar: false });
    load().catch(ex => { container.innerHTML = emptyHtml(ex.message, '<i class="ph ph-warning-circle"></i>'); });

    return { reload: load };
}
