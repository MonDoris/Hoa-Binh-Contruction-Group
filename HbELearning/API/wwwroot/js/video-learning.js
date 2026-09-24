// File: wwwroot/js/video-learning.js
// ============================================================================================
// TRÌNH PHÁT VIDEO CÓ CHÈN CÂU HỎI GIỮA CHỪNG ("video cắt khúc")
//
// Vì sao KHÔNG dùng <video controls> mặc định của trình duyệt: yêu cầu nghiệp vụ cần vẽ các điểm đánh
// dấu (dot) ngay trên thanh tiến trình để học viên biết trước phút thứ mấy có câu hỏi — thanh tiến trình
// mặc định của trình duyệt nằm trong shadow DOM, không chèn được gì vào. Nên dựng thanh điều khiển riêng.
//
// BA LỚP CHỐNG TUA QUA CÂU HỎI (không lớp nào đủ một mình):
//   1. Thanh tiến trình tự dựng: bấm/kéo vượt mốc chưa trả lời thì kẹp lại đúng mốc đó.
//   2. Sự kiện seeking/seeked của thẻ <video>: bắt mọi cách tua khác — phím tắt, cử chỉ trên điện thoại,
//      Picture-in-Picture, hoặc gọi thẳng video.currentTime từ DevTools.
//   3. timeupdate: chốt chặn cuối, phòng khi trình duyệt nhảy vị trí mà không bắn seeking (đã gặp trên
//      một số bản Safari khi phát file .webm).
//
// Chặn ở trình duyệt chỉ để trải nghiệm mượt — KHÔNG phải hàng rào bảo mật. Dữ liệu thật (đã trả lời
// đúng/sai, trả lời lúc nào) do server chấm và lưu; frontend không bao giờ nhận đáp án đúng trước khi
// học viên bấm gửi, nên mở DevTools cũng không xem trộm được đáp án.
// ============================================================================================

/** Sai số cho phép khi so vị trí phát với mốc câu hỏi (giây) — timeupdate chỉ bắn ~4 lần/giây. */
const VL_SEEK_TOLERANCE = 0.4;

/** Cửa sổ thời gian sau mốc mà câu hỏi "không chặn tua" vẫn được bật lên (giây). Ngoài cửa sổ này coi như
 *  học viên đã cố ý tua qua — không bật hộp thoại giữa chừng đoạn phim sau đó nữa. */
const VL_TRIGGER_WINDOW = 2;

/** Giãn cách tối thiểu giữa 2 lần nhắc "không tua được" để không spam thông báo (mili giây). */
const VL_NOTIFY_GAP_MS = 1500;

/** Bài "bắt buộc xem hết": khoảng media-time tối đa giữa 2 lần timeupdate còn được coi là ĐANG PHÁT bình
 *  thường (giây). Nhảy xa hơn mức này là máy bị khựng hoặc có ai đó vừa gán currentTime — không tính vào
 *  phần đã xem. Để rộng tay vì phát ở tốc độ 2x trên máy yếu cũng tạo ra khoảng cách vài giây. */
const VL_WATCH_STEP = 10;

/** Nhãn A/B/C/D... đứng trước mỗi đáp án — người học đọc và gọi tên đáp án nhanh hơn nhiều so với việc
 *  nhìn một ô tròn trống, và cũng là chỗ để vẽ trạng thái đã chọn/đúng/sai thay cho radio mặc định. */
const VL_OPTION_KEY = 'ABCDEFGHIJ';

/** Trạng thái một mốc câu hỏi. Dùng chung cho chấm trên thanh tiến trình và dòng trong danh sách câu hỏi để
 *  hai chỗ không bao giờ nói khác nhau về cùng một câu (màu chấm xanh mà danh sách ghi "chưa trả lời"). */
const MARKER_LABEL = {
    correct: 'Đã trả lời đúng',
    answered: 'Đã trả lời — chưa đúng',
    done: 'Đã trả lời',
    forced: 'Hết lượt trả lời — không tính điểm',
    retry: 'Chưa đúng — cần trả lời lại',
    locked: 'Bắt buộc trả lời',
    open: 'Chưa trả lời'
};

/** Chú giải màu của lưới số câu hỏi. Không có nó thì ba màu ô chỉ là ba màu, người học phải tự đoán. */
const VL_QGRID_LEGEND = `
    <div class="qgrid-legend">
        <span><i></i> Chưa trả lời</span>
        <span><i class="lg-answered"></i> Đã trả lời</span>
        <span><i class="lg-passed"></i> Đã đạt</span>
    </div>`;

/** Bỏ số 0 thừa sau dấu thập phân của điểm: 1.00 -> "1", 1.50 -> "1,5" (dấu phẩy kiểu Việt Nam). */
function vlFormatScore(value) {
    return Number(value || 0).toFixed(2).replace(/\.?0+$/, '').replace('.', ',') || '0';
}

/** Đổi số giây thành "m:ss" hoặc "h:mm:ss". */
function vlFormatTime(totalSecond) {
    if (!isFinite(totalSecond) || totalSecond < 0) totalSecond = 0;
    const hour = Math.floor(totalSecond / 3600);
    const minute = Math.floor((totalSecond % 3600) / 60);
    const second = Math.floor(totalSecond % 60);
    const pad = (n) => String(n).padStart(2, '0');
    return hour > 0 ? `${hour}:${pad(minute)}:${pad(second)}` : `${minute}:${pad(second)}`;
}

/**
 * Gom mốc câu hỏi vào chương chứa nó, để danh sách câu hỏi đọc được như một mục lục: tên chương là cấp cha,
 * câu hỏi rơi vào đoạn thời gian của chương nào thì nằm dưới chương đó.
 *
 * Trả về ĐỦ mọi chương, kể cả chương chưa gắn câu hỏi nào: mục lục cốt để nhìn thấy video gồm những phần
 * gì — bỏ đi chương rỗng thì người học tưởng đoạn đó không tồn tại, mà đó lại thường là đoạn dài nhất.
 *
 * Câu không thuộc chương nào (đoạn dẫn nhập, khoảng người soạn cố ý để trống) gom thành nhóm chương = null
 * đặt đúng chỗ của nó trên dòng thời gian, không dồn xuống cuối — dồn xuống cuối thì số thứ tự câu hỏi
 * trong mục lục nhảy ngược so với thứ tự thật sự gặp khi xem.
 *
 * @param {Array} checkpointList  Mốc câu hỏi, ĐÃ sắp theo atSecond tăng dần.
 * @param {Array} chapterList     Chương, ĐÃ sắp theo startSecond tăng dần.
 * @returns {Array<{chapter: object|null, startSecond: number, itemList: Array<{checkpoint: object, index: number}>}>}
 *          index là số thứ tự của câu trong CẢ bài (không phải trong chương) — số hiện trên ô lưới phải
 *          khớp với thứ tự gặp câu hỏi khi xem video, nếu đánh lại từ 1 trong mỗi chương thì bài có 3 chương
 *          sẽ có ba ô mang số 1, không còn gọi tên được câu nào với nhau nữa.
 */
function vlBuildChapterOutline(checkpointList, chapterList) {
    // Mốc kết thúc là mốc MỞ, cùng quy ước với chapterAt() trong trình phát: câu hỏi đúng giây 60 thuộc về
    // chương bắt đầu ở 60 chứ không thuộc chương kết thúc ở 60 — hai chương liền nhau mới không tranh nhau.
    const chapterOf = (second) =>
        chapterList.find(c => second >= c.startSecond && second < c.endSecond) || null;

    const groupList = chapterList.map(c => ({ chapter: c, startSecond: c.startSecond, itemList: [] }));
    let looseGroup = null;   // mạch câu ngoài chương đang mở, null = mạch vừa bị một câu có chương cắt đứt

    checkpointList.forEach((checkpoint, index) => {
        const chapter = chapterOf(checkpoint.atSecond);
        if (chapter) {
            groupList.find(g => g.chapter === chapter).itemList.push({ checkpoint, index });
            looseGroup = null;
            return;
        }
        if (!looseGroup) {
            looseGroup = { chapter: null, startSecond: checkpoint.atSecond, itemList: [] };
            groupList.push(looseGroup);
        }
        looseGroup.itemList.push({ checkpoint, index });
    });

    return groupList.sort((a, b) => a.startSecond - b.startSecond);
}

/**
 * Dựng trình phát vào một phần tử DOM.
 *
 * @param {object} options
 * @param {HTMLElement} options.container   Phần tử chứa trình phát (nội dung cũ bị thay thế).
 * @param {string}      options.src         Đường dẫn video.
 * @param {Array}       options.checkpointList  Mốc câu hỏi từ API /videolearning/lesson/{id}.
 * @param {Array}       [options.chapterList]   Bản đồ nội dung theo thời gian (chương / chủ đề), cùng API.
 *                                           Mỗi phần tử: { startSecond, endSecond, title, topic }.
 * @param {number}      [options.startAt]   Giây bắt đầu phát (vị trí xem dở lần trước).
 * @param {string}      [options.seekPolicy] 'Free' | 'WatchFull' | 'JumpToQuestion' — cách tua do người soạn
 *                                           bài chọn (Models/Lesson.VideoSeekPolicy).
 * @param {number}      [options.maxWatchedSecond] Mốc xa nhất đã xem ở những lần học trước (chế độ WatchFull).
 * @param {HTMLElement} [options.questionListContainer] Nơi vẽ danh sách câu hỏi. Bỏ trống = vẽ ngay trong
 *                                           khung trình phát; truyền vào để đặt danh sách xuống dưới bài học.
 * @param {Function}    [options.onAnswer]  async (checkpoint, selectedIdList) => kết quả chấm từ server.
 * @param {Function}    [options.onTick]    (currentSecond, maxWatchedSecond) => void, gọi mỗi giây khi đang phát.
 * @param {number}      [options.earnedScore] Điểm câu hỏi trong video đã đạt được từ trước.
 * @param {number}      [options.totalScore]  Tổng điểm của toàn bộ câu hỏi trong video này.
 * @param {boolean}     [options.preview]   true = chế độ xem thử của người soạn bài (không gọi API chấm).
 * @returns {{ video: HTMLVideoElement, destroy: Function }}
 */
function mountVideoLearning(options) {
    const { container, src, startAt = 0, onAnswer, onTick, preview = false } = options;
    const seekPolicy = options.seekPolicy || 'Free';
    let earnedScore = options.earnedScore || 0;
    let totalScore = options.totalScore || 0;

    // Mốc xa nhất đã thực sự xem. Lấy luôn cả vị trí xem dở lần trước làm sàn: bài học tạo từ trước khi có
    // cột MAX_WATCHED_SECOND thì cột đó bằng 0, nếu không nhận sàn này thì học viên đang xem dở phút 20 sẽ
    // bị ném về giây 0 ngay lần mở bài đầu tiên sau khi bật chế độ "bắt buộc xem hết".
    let maxWatched = Math.max(options.maxWatchedSecond || 0, startAt || 0);
    // Vị trí ở lần timeupdate trước — dùng để phân biệt "đang phát" với "vừa bị tua". null = không rõ
    // (vừa tua xong / chưa phát), khi đó lần lấy mẫu kế tiếp không được tính là đã xem.
    let lastSampleSecond = null;

    // Bản sao riêng để đánh dấu đã vượt qua / đã bỏ qua mà không đụng vào dữ liệu gốc của trang gọi
    const checkpointList = (options.checkpointList || [])
        .map(cp => ({ ...cp, skipped: false }))
        .sort((a, b) => a.atSecond - b.atSecond);

    // Bản đồ nội dung: đoạn nào của video nói về chương gì. Lọc bỏ đoạn hỏng ngay từ đây (mốc kết thúc
    // không lớn hơn mốc bắt đầu) để phần vẽ và phần dò bên dưới không phải phòng thủ lại lần nữa.
    const chapterList = (options.chapterList || [])
        .filter(c => Number(c.endSecond) > Number(c.startSecond))
        .sort((a, b) => a.startSecond - b.startSecond);

    container.innerHTML = `
        <div class="vl-player">
            <div class="vl-stage">
                <video class="vl-video" src="${esc(src)}" preload="metadata" playsinline></video>
                <button type="button" class="vl-big-play" aria-label="Phát video"><i class="ph-fill ph-play"></i></button>
                <div class="vl-ask" hidden></div>
            </div>
            <div class="vl-bar">
                <button type="button" class="vl-play" aria-label="Phát / tạm dừng"><i class="ph-fill ph-play"></i></button>
                <div class="vl-track" role="slider" aria-label="Tiến trình video" tabindex="0">
                    <div class="vl-track-line">
                        <div class="vl-buffered"></div>
                        <div class="vl-locked" hidden></div>
                        <div class="vl-played"></div>
                        <div class="vl-knob"></div>
                        <!-- Vạch ngăn giữa các chương, nằm TRONG thanh nên không ăn sự kiện chuột của
                             thao tác kéo tua (pointer-events: none trong CSS). -->
                        <div class="vl-chapter-layer"></div>
                    </div>
                    <div class="vl-marker-layer"></div>
                </div>
                <span class="vl-time">0:00 / 0:00</span>
                <button type="button" class="vl-rate" aria-label="Tốc độ phát">1x</button>
                <button type="button" class="vl-note" aria-label="Ghi chú tại đây" hidden>
                    <i class="ph ph-note-pencil"></i><span class="vl-note-text">Ghi chú tại đây</span>
                </button>
                <button type="button" class="vl-mute" aria-label="Tắt / bật tiếng"><i class="ph-fill ph-speaker-high"></i></button>
                <button type="button" class="vl-full" aria-label="Toàn màn hình"><i class="ph ph-corners-out"></i></button>
            </div>
            <!-- Nhãn "đang xem đoạn nào": nằm ngay dưới thanh điều khiển chứ không đè lên hình. Đè lên hình
                 thì phải tự ẩn đi sau vài giây cho khỏi che nội dung, mà ẩn rồi thì học viên tua tới giữa
                 video lại không biết mình đang ở đâu — đúng lúc cần nhất thì không có. -->
            <div class="vl-chapter" hidden></div>
            <div class="vl-legend">
                <div class="vl-legend-text"></div>
                <div class="vl-score" hidden></div>
            </div>
            <div class="vl-qlist-slot"></div>
        </div>`;

    const root = container.querySelector('.vl-player');
    const video = root.querySelector('.vl-video');
    const askBox = root.querySelector('.vl-ask');
    const track = root.querySelector('.vl-track');
    const playedEl = root.querySelector('.vl-played');
    const bufferedEl = root.querySelector('.vl-buffered');
    const lockedEl = root.querySelector('.vl-locked');
    const knobEl = root.querySelector('.vl-knob');
    const markerLayer = root.querySelector('.vl-marker-layer');
    const chapterLayer = root.querySelector('.vl-chapter-layer');
    const chapterBar = root.querySelector('.vl-chapter');
    const timeEl = root.querySelector('.vl-time');
    const legendEl = root.querySelector('.vl-legend');
    const legendTextEl = root.querySelector('.vl-legend-text');
    const scoreEl = root.querySelector('.vl-score');
    const playButton = root.querySelector('.vl-play');
    const bigPlayButton = root.querySelector('.vl-big-play');
    // Danh sách câu hỏi có thể nằm ngoài trình phát (VD trong cây nội dung bên phải) — trang gọi truyền phần
    // tử vào. Để let chứ không const: trang chủ quản có thể vẽ lại vùng đó (cây nội dung đóng/mở nhánh là vẽ
    // lại cả cây), khi ấy ô cũ đã lìa khỏi DOM và phải trỏ sang ô mới qua remountQuestionList().
    let questionListBox = options.questionListContainer || root.querySelector('.vl-qlist-slot');

    let activeCheckpoint = null;   // câu hỏi đang hiện hộp thoại
    let questionListStamp = '';    // dấu vân của danh sách câu hỏi đang hiển thị (xem refreshQuestionList)
    let lastNotifyAt = 0;
    let lastTickSecond = -1;
    let destroyed = false;

    // ---------- Trạng thái chặn tua ----------

    /** Mốc câu hỏi này có thực sự chặn tua hay không. Cờ blockSeek do người soạn đặt cho riêng mốc đó, nhưng
     *  bài học chọn "cho bấm tới câu hỏi" thì nó bị vô hiệu: đã cho học viên nhảy thẳng tới câu 3 mà vẫn khóa
     *  thanh tiến trình ở câu 1 thì hai lựa chọn đá nhau, bấm tới đâu cũng bị kéo ngược về. */
    function blocksSeek(checkpoint) {
        return checkpoint.blockSeek && seekPolicy !== 'JumpToQuestion';
    }

    /** Mốc thời gian xa nhất học viên được phép tua tới. Hai nguồn giới hạn, lấy cái ngặt hơn:
     *  mốc câu hỏi chặn-tua đầu tiên chưa vượt qua, và (chế độ "bắt buộc xem hết") phần đã thực sự xem. */
    function seekCeiling() {
        const blocking = checkpointList.find(cp => !cp.isPassed && blocksSeek(cp));
        const questionCeiling = blocking ? blocking.atSecond : Infinity;
        return seekPolicy === 'WatchFull' ? Math.min(questionCeiling, maxWatched) : questionCeiling;
    }

    /** Ghi nhận phần đã xem. Chỉ nhích lên khi video ĐANG PHÁT và bước nhảy đủ nhỏ để chắc chắn là do phát
     *  bình thường — mọi thao tác tua đều đặt lastSampleSecond = null nên không bao giờ được tính là đã xem,
     *  đó là chỗ giữ cho chế độ "bắt buộc xem hết" không bị lách bằng cách kéo thanh tiến trình từng nhịp. */
    function trackWatched() {
        const now = video.currentTime;
        const isPlayingForward = !video.paused && !video.seeking && lastSampleSecond !== null
            && now > lastSampleSecond && now - lastSampleSecond <= VL_WATCH_STEP;

        if (isPlayingForward && now > maxWatched) maxWatched = now;
        lastSampleSecond = video.paused || video.seeking ? null : now;
    }

    /** Kéo vị trí phát về đúng mốc câu hỏi nếu đang vượt quá giới hạn. Trả về true khi đã phải kéo lại. */
    function guardSeek() {
        const ceiling = seekCeiling();
        if (!isFinite(ceiling) || video.currentTime <= ceiling + VL_SEEK_TOLERANCE) return false;

        video.currentTime = ceiling;
        notifyBlocked(ceiling);
        return true;
    }

    /** Nhắc vì sao không tua qua được. Hai lý do khác hẳn nhau nên không dùng chung một câu: bị chặn vì
     *  chưa trả lời câu hỏi thì học viên phải làm câu hỏi, còn bị chặn vì bài bắt buộc xem hết thì có làm
     *  gì cũng vô ích ngoài việc ngồi xem tiếp — báo sai lý do là đẩy họ đi tìm câu hỏi không tồn tại. */
    function notifyBlocked(ceiling) {
        const now = Date.now();
        if (now - lastNotifyAt < VL_NOTIFY_GAP_MS) return;
        lastNotifyAt = now;

        const blocking = checkpointList.find(cp => !cp.isPassed && blocksSeek(cp));
        toastErr(blocking && blocking.atSecond <= ceiling + VL_SEEK_TOLERANCE
            ? `Cần trả lời câu hỏi ở phút ${vlFormatTime(blocking.atSecond)} trước khi xem tiếp.`
            : 'Bài học này yêu cầu xem hết video — bạn chỉ tua được trong phần đã xem.');
    }

    // ---------- Bản đồ nội dung (chương / chủ đề) ----------

    /** Chương chứa giây `second`, hoặc null nếu giây đó không thuộc chương nào (đoạn dẫn nhập, lời chào
     *  cuối, khoảng trống người soạn cố ý để lại).
     *
     *  Mốc kết thúc là mốc MỞ: giây 60 thuộc về chương bắt đầu ở 60, không thuộc chương kết thúc ở 60.
     *  Không quy ước như vậy thì ở đúng ranh giới giữa hai chương liền nhau, cả hai cùng khớp và nhãn nhấp
     *  nháy qua lại giữa hai tên mỗi lần timeupdate. */
    function chapterAt(second) {
        return chapterList.find(c => second >= c.startSecond && second < c.endSecond) || null;
    }

    /** Khóa nhận dạng một chương, dùng chung cho nhãn dưới khung video và cho mục lục bên cạnh — hai chỗ
     *  phải sáng lên đúng cùng một chương, đánh khóa khác nhau là có lúc lệch nhau một dòng.
     *  Chương do trang xem thử của người soạn dựng lên chưa có id, khi ấy cặp mốc thời gian là thứ duy nhất
     *  phân biệt được (hai chương không bao giờ trùng cả mốc đầu lẫn mốc cuối). */
    function chapterKeyOf(chapter) {
        return String(chapter.id ?? `${chapter.startSecond}-${chapter.endSecond}`);
    }

    /** Nhãn đầy đủ của một chương: "Chủ đề 1 · Tên chương". Chủ đề để trống thì chỉ còn tên chương. */
    function chapterLabel(chapter) {
        return chapter.topic ? `${chapter.topic} · ${chapter.title}` : chapter.title;
    }

    /** Vẽ dải chương lên thanh tiến trình: mỗi chương một khối, hở ra đúng những đoạn không thuộc chương
     *  nào. Cần biết thời lượng mới quy ra được phần trăm nên chỉ chạy sau loadedmetadata. */
    function renderChapterLayer() {
        if (!chapterLayer) return;

        const duration = video.duration;
        if (!chapterList.length || !isFinite(duration) || duration <= 0) {
            chapterLayer.innerHTML = '';
            return;
        }

        chapterLayer.innerHTML = chapterList
            .filter(c => c.startSecond < duration)
            .map(c => {
                // Chương cuối thường được đặt kết thúc ở một con số tròn vượt quá thời lượng thật — kẹp lại
                // để khối không tràn khỏi thanh.
                const end = Math.min(c.endSecond, duration);
                const left = (c.startSecond / duration) * 100;
                const width = ((end - c.startSecond) / duration) * 100;
                return `<span class="vl-chapter-seg" style="left:${left}%;width:${width}%"
                              title="${esc(vlFormatTime(c.startSecond))}–${esc(vlFormatTime(end))} · ${esc(chapterLabel(c))}"></span>`;
            }).join('');
    }

    /** Cập nhật nhãn "đang xem đoạn nào" theo vị trí phát.
     *
     *  Chỉ đụng vào DOM khi chương thật sự đổi: hàm này chạy ở mọi nhịp timeupdate (~4 lần/giây) và cả
     *  trong lúc kéo thanh tua, ghi đè innerHTML mỗi nhịp là ép trình duyệt vẽ lại một khối chữ liên tục
     *  suốt thời lượng video. */
    let shownChapterId = undefined;
    function renderChapterBar() {
        if (!chapterBar) return;

        if (!chapterList.length) { chapterBar.hidden = true; return; }

        const chapter = chapterAt(video.currentTime);
        const id = chapter ? chapterKeyOf(chapter) : null;
        if (id === shownChapterId) return;
        shownChapterId = id;

        // Ngoài mọi chương (đoạn dẫn nhập / lời chào cuối): ẩn hẳn thanh thay vì hiện "—". Một dòng trống
        // giữ chỗ trông như dữ liệu bị lỗi, còn thanh biến mất thì đọc đúng là "đoạn này không có chương".
        if (!chapter) { chapterBar.hidden = true; return; }

        chapterBar.hidden = false;
        chapterBar.innerHTML = `
            <i class="ph ph-bookmark-simple"></i>
            <span class="vl-chapter-time">${esc(vlFormatTime(chapter.startSecond))}–${esc(vlFormatTime(chapter.endSecond))}</span>
            ${chapter.topic ? `<span class="vl-chapter-topic">${esc(chapter.topic)}</span>` : ''}
            <b class="vl-chapter-title">${esc(chapter.title)}</b>`;
    }

    // ---------- Vẽ giao diện ----------

    function renderMarkerList() {
        const duration = video.duration;
        if (!isFinite(duration) || duration <= 0) return;

        markerLayer.innerHTML = checkpointList
            .filter(cp => cp.atSecond <= duration)
            .map(cp => {
                const state = markerStateOf(cp);
                return `<span class="vl-marker ${state}" style="left:${(cp.atSecond / duration) * 100}%"
                              title="${vlFormatTime(cp.atSecond)} · ${MARKER_LABEL[state]}: ${esc(cp.content)}"></span>`;
            }).join('');

        const remaining = checkpointList.filter(cp => !cp.isPassed).length;
        legendTextEl.innerHTML = checkpointList.length
            ? `<i class="ph ph-seal-question"></i> Video có <b>${checkpointList.length}</b> câu hỏi chèn giữa chừng
               ${remaining ? `· còn <b>${remaining}</b> câu chưa trả lời` : '· <span class="vl-legend-done">bạn đã trả lời hết</span>'}
               <span class="vl-legend-hint">— các chấm trên thanh tiến trình là vị trí câu hỏi.</span>`
            : '';
        renderScore();
        renderQuestionList();
    }

    /** Thanh điểm câu hỏi trong video. Chỉ hiện khi bài học thực sự có chấm điểm (tổng điểm > 0) — bài chỉ
     *  chèn câu hỏi để nhắc bài, không đặt điểm, thì một ô "0/0 điểm" chỉ gây hoang mang. */
    function renderScore() {
        const show = totalScore > 0 && !preview;
        scoreEl.hidden = !show;
        legendEl.classList.toggle('vl-legend-empty', !show && !legendTextEl.innerHTML.trim());
        if (!show) return;
        // Chưa trả lời câu nào thì để màu trung tính: 0/3 tô đỏ ngay từ lúc mở bài trông như học viên đang
        // bị điểm kém, trong khi thực ra họ còn chưa bắt đầu.
        const started = checkpointList.some(cp => cp.isAnswered);
        const percent = Math.round((earnedScore / totalScore) * 100);
        scoreEl.className = `vl-score ${!started ? '' : percent >= 70 ? 'good' : percent >= 40 ? 'mid' : 'low'}`;
        scoreEl.innerHTML = `<i class="ph-fill ph-star"></i> Điểm câu hỏi
            <b>${vlFormatScore(earnedScore)}/${vlFormatScore(totalScore)}</b>`;
    }

    /** Trạng thái của một mốc câu hỏi — xem MARKER_LABEL. Tách ra 7 trạng thái vì "đã trả lời" và "đã qua"
     *  là hai chuyện khác nhau: câu bắt buộc đúng trả lời sai thì vẫn đã trả lời nhưng CHƯA qua (phải làm
     *  lại), còn câu không bắt buộc đúng trả lời sai thì đã qua nhưng không có điểm. Gộp lại là báo sai
     *  kết quả cho học viên ngay trên chấm đánh dấu lẫn trong danh sách.
     *  Câu không chấm điểm (score = 0) chỉ nói được "đã trả lời": dữ liệu trả cho học viên không có cờ
     *  đúng/sai, mà suy từ điểm thì câu 0 điểm lúc nào cũng ra "sai". */
    function markerStateOf(checkpoint) {
        if (checkpoint.isForcedPass) return 'forced';
        if (!checkpoint.isPassed)
            return checkpoint.isAnswered ? 'retry' : (blocksSeek(checkpoint) ? 'locked' : 'open');
        if (checkpoint.score <= 0) return 'done';
        return checkpoint.earnedScore > 0 ? 'correct' : 'answered';
    }

    /** Học viên có được nhảy thẳng tới mốc này không.
     *  - Bài chọn "cho bấm tới câu hỏi": luôn được, đó chính là mục đích của lựa chọn đó.
     *  - Bài khác: chỉ được khi mốc nằm trong phần đã mở khóa — nút vẫn hiện nhưng khóa lại kèm lý do,
     *    để học viên biết câu đó tồn tại và vì sao chưa vào được, thay vì thấy một danh sách cụt. */
    function canJumpTo(checkpoint) {
        if (seekPolicy === 'JumpToQuestion') return true;
        return checkpoint.atSecond <= seekCeiling() + VL_SEEK_TOLERANCE;
    }

    /** Có được tua thẳng tới giây này không. Dùng cho tên chương trong mục lục — mục lục đi cùng một luật
     *  với lưới câu hỏi, nếu không nó thành cửa sau để nhảy qua câu hỏi bắt buộc chỉ bằng cách bấm tên chương. */
    function canJumpToSecond(second) {
        if (seekPolicy === 'JumpToQuestion') return true;
        return second <= seekCeiling() + VL_SEEK_TOLERANCE;
    }

    /** Vì sao chưa tua tới được giây này. Hai lý do khác hẳn nhau nên không dùng chung một câu — nói đúng lý
     *  do thì học viên biết phải làm gì tiếp (trả lời câu trước / ngồi xem tiếp), nói sai thì họ đi tìm việc
     *  không có thật. */
    function seekBlockReason(second) {
        const blocking = checkpointList.find(cp => !cp.isPassed && blocksSeek(cp) && cp.atSecond < second);
        return blocking
            ? `Cần trả lời câu hỏi ở phút ${vlFormatTime(blocking.atSecond)} trước đã.`
            : 'Bài học yêu cầu xem hết video — hãy xem tiếp tới mốc này.';
    }

    function jumpReason(checkpoint) {
        return canJumpTo(checkpoint) ? '' : seekBlockReason(checkpoint.atSecond);
    }

    /** Nhảy tới mốc câu hỏi rồi mở luôn hộp thoại. Câu đã trả lời xong thì chỉ tua tới để xem lại đoạn video
     *  đó, không hỏi lại — bắt làm lại một câu đã đúng chỉ tổ làm hỏng điểm đã có. */
    function jumpToCheckpoint(checkpoint) {
        if (!canJumpTo(checkpoint)) { toastErr(jumpReason(checkpoint)); return; }

        video.pause();
        lastSampleSecond = null;
        video.currentTime = checkpoint.atSecond;
        renderProgress();

        // Đã vượt qua thì chỉ tua tới xem lại đoạn video đó, không hỏi lại: bắt làm lại một câu đã đúng
        // chỉ tổ làm hỏng điểm đã có. Trả lời rồi mà chưa qua (câu bắt buộc đúng, còn lượt) thì mở lại
        // đúng hộp thoại đó cho làm tiếp.
        if (checkpoint.isPassed) {
            video.play().catch(() => {});
            return;
        }
        openQuestion(checkpoint);
    }

    /** Tua tới đầu một chương rồi phát tiếp. Cố ý KHÔNG mở hộp thoại nào: chương là mốc nội dung, câu hỏi
     *  bên trong nó vẫn tự bật lên như thường khi video chạy tới đúng mốc của từng câu. */
    function jumpToSecond(second) {
        if (!canJumpToSecond(second)) { toastErr(seekBlockReason(second)); return; }

        video.pause();
        lastSampleSecond = null;
        video.currentTime = second;
        renderProgress();
        video.play().catch(() => {});
    }

    /** Dấu vân của mọi thứ danh sách câu hỏi đang hiển thị. Danh sách phải đổi theo tiến độ xem (câu phía
     *  trước vừa mở khóa thì nút "Tới câu hỏi" của nó phải bật lên), nhưng vẽ lại ở mọi nhịp timeupdate thì
     *  vừa phí vừa làm mất tiêu điểm bàn phím đang đặt trên nút — nên chỉ vẽ lại khi dấu vân đổi. */
    function questionListFingerprint() {
        // Trạng thái khóa của từng chương phải nằm trong dấu vân: ở bài "bắt buộc xem hết", mốc được phép
        // tua tới nhích lên liên tục theo phần đã xem, một chương có thể mở khóa mà không câu hỏi nào đổi
        // trạng thái — thiếu phần này thì tên chương cứ mờ mãi dù đã xem qua nó từ lâu.
        const chapterStamp = chapterList.map(c => (canJumpToSecond(c.startSecond) ? 1 : 0)).join('');

        return checkpointList
            .map(cp => `${cp.id}:${markerStateOf(cp)}:${canJumpTo(cp) ? 1 : 0}:${cp.earnedScore || 0}`)
            .join('|') + `#${earnedScore}/${totalScore}#${chapterStamp}`;
    }

    function refreshQuestionList() {
        if (questionListBox && questionListFingerprint() !== questionListStamp) renderQuestionList();
    }

    /** Mục lục câu hỏi trong video: mỗi chương một cụm, câu hỏi nằm dưới đúng chương chứa mốc của nó. Có mặt
     *  vì phần lớn học viên không xem hết video: nhìn được ngay bài này gồm những chương gì, mỗi chương có
     *  mấy câu, đã làm tới đâu, còn thiếu câu nào — thay vì phải phát lại cả video mới biết.
     *
     *  Cố ý KHÔNG có tiêu đề "Câu hỏi trong video", bộ đếm câu và điểm ở đây: cả ba đã nằm sẵn ngay dưới
     *  khung video (xem renderLegend và renderScore), lặp lại lần nữa chỉ tổ đẩy lưới xuống thấp. Cách tua
     *  cũng vậy — lý do một câu chưa bấm được nằm trong tooltip của chính ô đó (xem renderQuestionCell). */
    function renderQuestionList() {
        if (!questionListBox) return;
        questionListStamp = questionListFingerprint();

        // Không câu hỏi mà cũng không chương thì không có mục lục nào để vẽ. Chỉ có chương thôi vẫn vẽ:
        // bản đồ nội dung của video tự nó đã là thứ đáng xem, không phải phần phụ của danh sách câu hỏi.
        if (!checkpointList.length && !chapterList.length) {
            questionListBox.innerHTML = '';
            return;
        }

        // Có bản đồ chương thì xếp câu hỏi thành mục lục (chương làm cấp cha); không có thì giữ nguyên một
        // lưới phẳng — dựng một cấp cha rỗng chỉ để bọc lấy đúng cái lưới đó là thêm nhiễu chứ không thêm tin.
        const body = chapterList.length
            ? vlBuildChapterOutline(checkpointList, chapterList).map(renderQuestionGroup).join('')
            : `<div class="qgrid">${checkpointList.map((cp, index) => renderQuestionCell(cp, index)).join('')}</div>`;

        questionListBox.innerHTML = `
            <div class="vl-qlist">
                ${body}
                ${checkpointList.length ? VL_QGRID_LEGEND : ''}
            </div>`;

        questionListBox.querySelectorAll('[data-vl-goto]').forEach(button => {
            button.addEventListener('click', () => {
                const checkpoint = checkpointList.find(cp => cp.id === +button.dataset.vlGoto);
                if (checkpoint) jumpToCheckpoint(checkpoint);
            });
        });

        questionListBox.querySelectorAll('[data-vl-seek]').forEach(button => {
            button.addEventListener('click', () => jumpToSecond(+button.dataset.vlSeek));
        });

        // DOM vừa dựng lại nên không còn dấu của lần trước — xóa trí nhớ rồi đánh dấu lại ngay, không đợi
        // nhịp timeupdate kế tiếp (video đang tạm dừng thì nhịp đó không bao giờ tới, mục lục ngồi trơ
        // không có chương nào sáng).
        markedChapterKey = undefined;
        renderActiveChapter();
    }

    /**
     * Một chương trong mục lục: tên chương làm dòng cha, lưới số câu hỏi của chương nằm ngay dưới.
     *
     * Dòng chương bấm được để tua thẳng tới đầu chương — mục lục mà chỉ đọc được thì học viên vẫn phải kéo
     * thanh tiến trình đi tìm. Chương chưa mở khóa thì nút mờ đi kèm lý do trong tooltip, giống hệt ô câu hỏi
     * bị khóa, thay vì biến mất khỏi mục lục.
     */
    function renderQuestionGroup(group) {
        const grid = group.itemList.length
            ? `<div class="qgrid">${group.itemList.map(x => renderQuestionCell(x.checkpoint, x.index)).join('')}</div>`
            : `<div class="qgroup-empty">Đoạn này không có câu hỏi</div>`;

        // Nhóm câu ngoài chương: không có gì để tua tới cho chắc chắn đúng (đoạn này không có mốc bắt đầu
        // của riêng nó), nên chỉ là một dòng nhãn.
        if (!group.chapter)
            return `<div class="qgroup">
                <div class="qgroup-head plain">
                    <i class="ph ph-dots-three-circle"></i>
                    <span class="qgroup-title">Ngoài chương</span>
                    <span class="qgroup-count">${group.itemList.length}</span>
                </div>
                ${grid}
            </div>`;

        const chapter = group.chapter;
        const locked = !canJumpToSecond(chapter.startSecond);
        const detail = [
            `${vlFormatTime(chapter.startSecond)}–${vlFormatTime(chapter.endSecond)}`,
            chapter.topic || null,
            `${group.itemList.length} câu hỏi`,
            locked ? seekBlockReason(chapter.startSecond) : 'Bấm để xem từ đầu chương'
        ].filter(Boolean).join(' · ');

        return `<div class="qgroup" data-vl-chapter="${esc(chapterKeyOf(chapter))}">
            <button type="button" class="qgroup-head ${locked ? 'locked' : ''}"
                    data-vl-seek="${chapter.startSecond}" ${locked ? 'disabled' : ''}
                    title="${esc(chapter.title)} — ${esc(detail)}">
                <i class="ph ph-bookmark-simple"></i>
                <span class="qgroup-title">${esc(chapter.title)}</span>
                <span class="qgroup-time">${esc(vlFormatTime(chapter.startSecond))}</span>
                ${group.itemList.length ? `<span class="qgroup-count">${group.itemList.length}</span>` : ''}
            </button>
            ${chapter.topic ? `<div class="qgroup-topic">${esc(chapter.topic)}</div>` : ''}
            ${grid}
        </div>`;
    }

    /**
     * Một ô số trong lưới câu hỏi. Cả nội dung câu hỏi, mốc giây, điểm và trạng thái đều dồn vào thuộc tính
     * title: lưới cốt để nhìn một cái là nắm được đề có mấy câu và còn sót câu nào, còn chi tiết thì rê chuột
     * (hoặc bấm vào để tới thẳng câu đó trong video) là ra.
     */
    function renderQuestionCell(checkpoint, index) {
        const state = markerStateOf(checkpoint);
        const locked = !canJumpTo(checkpoint);
        const cls = checkpoint.isPassed ? 'passed' : checkpoint.isAnswered ? 'answered' : '';
        const isCurrent = activeCheckpoint && activeCheckpoint.id === checkpoint.id;

        const detail = [
            vlFormatTime(checkpoint.atSecond),
            MARKER_LABEL[state],
            checkpoint.score > 0
                ? `${checkpoint.isAnswered ? vlFormatScore(checkpoint.earnedScore) + '/' : ''}${vlFormatScore(checkpoint.score)} điểm`
                : null,
            checkpoint.mustAnswerCorrectly ? 'Phải trả lời đúng' : null,
            locked ? jumpReason(checkpoint) : null
        ].filter(Boolean).join(' · ');

        return `<button type="button" class="qgrid-item ${cls} ${isCurrent ? 'current' : ''} ${locked ? 'locked' : ''}"
                        data-vl-goto="${checkpoint.id}" ${locked ? 'disabled' : ''}
                        title="Câu ${index + 1}: ${esc(checkpoint.content)} — ${esc(detail)}">${index + 1}</button>`;
    }

    /**
     * Tô sáng chương đang phát trong mục lục bên cạnh: xem tới đâu thì cụm chương đó sáng lên, để học viên
     * nhìn mục lục là biết mình đang ở phần nào của video mà không phải đối chiếu mốc giây.
     *
     * Chỉ đổi class chứ không vẽ lại mục lục: hàm này chạy ở mọi nhịp timeupdate (~4 lần/giây) và cả trong
     * lúc kéo thanh tua — dựng lại innerHTML với nhịp đó thì mất luôn tiêu điểm bàn phím đang đặt trên một ô
     * câu hỏi, và chuột đang rê trên ô nào cũng bị rơi ra khỏi ô đó.
     *
     * markedChapterKey = undefined nghĩa là "chưa biết mục lục đang đánh dấu chương nào" — renderQuestionList
     * đặt lại giá trị này sau mỗi lần vẽ mới, vì DOM vừa dựng lại thì không còn class nào từ lần trước.
     */
    let markedChapterKey = undefined;
    function renderActiveChapter() {
        if (!questionListBox || !chapterList.length) return;

        const chapter = chapterAt(video.currentTime);
        const key = chapter ? chapterKeyOf(chapter) : null;
        if (key === markedChapterKey) return;
        markedChapterKey = key;

        questionListBox.querySelectorAll('.qgroup[data-vl-chapter]').forEach(group => {
            group.classList.toggle('current', group.dataset.vlChapter === key);
        });
    }

    function renderProgress() {
        const duration = video.duration;
        if (!isFinite(duration) || duration <= 0) return;

        const percent = (video.currentTime / duration) * 100;
        playedEl.style.width = `${percent}%`;
        knobEl.style.left = `${percent}%`;
        timeEl.textContent = `${vlFormatTime(video.currentTime)} / ${vlFormatTime(duration)}`;

        if (video.buffered.length)
            bufferedEl.style.width = `${(video.buffered.end(video.buffered.length - 1) / duration) * 100}%`;

        // Tô mờ phần video chưa được phép xem để học viên hiểu vì sao kéo không qua
        const ceiling = seekCeiling();
        if (isFinite(ceiling) && ceiling < duration) {
            lockedEl.hidden = false;
            lockedEl.style.left = `${(ceiling / duration) * 100}%`;
            lockedEl.style.right = '0';
        } else {
            lockedEl.hidden = true;
        }

        renderChapterBar();
        refreshQuestionList();
        renderActiveChapter();
    }

    function setPlayIcon() {
        const icon = video.paused ? 'ph-fill ph-play' : 'ph-fill ph-pause';
        playButton.innerHTML = `<i class="${icon}"></i>`;
        bigPlayButton.hidden = !video.paused || !!activeCheckpoint;
        root.classList.toggle('playing', !video.paused);
    }

    // ---------- Hộp thoại câu hỏi ----------

    /** Số lượt trả lời còn lại của một câu hỏi; null = không giới hạn (hoặc câu không bắt buộc đúng). */
    function remainingAttemptOf(checkpoint) {
        if (!checkpoint.mustAnswerCorrectly || !checkpoint.maxAttempt) return null;
        return Math.max(0, checkpoint.maxAttempt - (checkpoint.tryCount || 0));
    }

    function openQuestion(checkpoint) {
        activeCheckpoint = checkpoint;
        video.pause();
        root.classList.add('vl-blocked');
        bigPlayButton.hidden = true;

        const isMulti = checkpoint.questionType === 'MultipleChoice';
        const inputType = isMulti ? 'checkbox' : 'radio';
        const index = checkpointList.indexOf(checkpoint) + 1;
        const remaining = remainingAttemptOf(checkpoint);

        askBox.hidden = false;
        askBox.innerHTML = `
            <div class="vl-ask-card" role="dialog" aria-modal="true" aria-label="Câu hỏi trong video">
                <div class="vl-ask-head">
                    <span class="vl-ask-step">Câu ${index}/${checkpointList.length}</span>
                    <span class="vl-ask-at"><i class="ph ph-clock"></i> ${vlFormatTime(checkpoint.atSecond)}</span>
                    ${checkpoint.score > 0 ? `<span class="vl-ask-flag score"><i class="ph-fill ph-star"></i> ${vlFormatScore(checkpoint.score)} điểm</span>` : ''}
                    ${checkpoint.mustAnswerCorrectly
                        ? '<span class="vl-ask-flag must"><i class="ph ph-lock-key"></i> Phải trả lời đúng</span>'
                        : '<span class="vl-ask-flag"><i class="ph ph-info"></i> Trả lời để xem tiếp</span>'}
                    ${remaining !== null
                        ? `<span class="vl-ask-flag tries ${remaining <= 1 ? 'danger' : ''}" data-vl-tries>
                               <i class="ph ph-arrow-counter-clockwise"></i> Còn ${remaining}/${checkpoint.maxAttempt} lượt</span>`
                        : ''}
                </div>
                <h4 class="vl-ask-content">${esc(checkpoint.content)}</h4>
                ${checkpoint.hint ? `<div class="vl-ask-hint"><i class="ph-fill ph-lightbulb"></i><span>${esc(checkpoint.hint)}</span></div>` : ''}
                <div class="vl-opt-list">
                    ${checkpoint.optionList.map((option, i) => `
                        <label class="vl-opt">
                            <input type="${inputType}" name="vl-opt" value="${option.id}">
                            <span class="vl-opt-key">${VL_OPTION_KEY[i] || i + 1}</span>
                            <span class="vl-opt-text">${esc(option.content)}</span>
                            <i class="vl-opt-state"></i>
                        </label>`).join('')}
                </div>
                ${isMulti ? '<div class="vl-ask-note"><i class="ph ph-list-checks"></i> Câu hỏi này có thể có nhiều đáp án đúng.</div>' : ''}
                <div class="vl-ask-result" hidden></div>
                <div class="vl-ask-foot">
                    <span class="vl-ask-try">${checkpoint.tryCount > 0 ? `Đã trả lời ${checkpoint.tryCount} lần` : ''}</span>
                    <div class="vl-ask-actions">
                        ${(!checkpoint.blockSeek && !checkpoint.mustAnswerCorrectly)
                            ? '<button type="button" class="btn ghost" data-vl-skip>Bỏ qua câu này</button>' : ''}
                        <button type="button" class="btn" data-vl-submit><i class="ph-bold ph-paper-plane-right"></i> Gửi câu trả lời</button>
                    </div>
                </div>
            </div>`;

        askBox.querySelector('[data-vl-submit]').addEventListener('click', submitAnswer);
        askBox.querySelector('[data-vl-skip]')?.addEventListener('click', () => {
            checkpoint.skipped = true;
            closeQuestion(true);
        });
        askBox.scrollTop = 0;
        fitStageToAsk();
        setPlayIcon();
    }

    /** Nong khung video cao bằng thẻ câu hỏi khi thẻ cao hơn khung.
     *
     *  Hộp thoại phủ tuyệt đối lên khung video, mà khung video bị giới hạn max-height — câu hỏi 4 đáp án
     *  dài kèm gợi ý là cao hơn khung, khiến đúng cái nút "Gửi câu trả lời" bị đẩy ra ngoài tầm nhìn.
     *  Overlay có cuộn được, nhưng bắt học viên cuộn mới thấy nút gửi là một cái bẫy giao diện rõ ràng.
     *  Phải đo sau khi trình duyệt đã dựng xong (requestAnimationFrame) vì offsetHeight lúc vừa gán
     *  innerHTML còn là 0. */
    function fitStageToAsk() {
        requestAnimationFrame(() => {
            const card = askBox.querySelector('.vl-ask-card');
            if (!card) return;
            root.style.setProperty('--vl-ask-height', `${card.offsetHeight + 36}px`);
        });
    }

    async function submitAnswer() {
        const checkpoint = activeCheckpoint;
        if (!checkpoint) return;

        const selectedIdList = [...askBox.querySelectorAll('input[name="vl-opt"]:checked')].map(el => +el.value);
        if (!selectedIdList.length) { toastErr('Bạn chưa chọn đáp án nào.'); return; }

        const submitButton = askBox.querySelector('[data-vl-submit]');
        submitButton.disabled = true;

        let result;
        try {
            result = preview
                ? { isCorrect: true, canContinue: true, message: 'Chế độ xem thử — câu trả lời không được lưu.', correctOptionIdList: [] }
                : await onAnswer(checkpoint, selectedIdList);
        } catch (ex) {
            toastErr(ex.message);
            submitButton.disabled = false;
            return;
        }

        checkpoint.tryCount = result.tryCount ?? (checkpoint.tryCount + 1);
        checkpoint.isAnswered = true;
        checkpoint.earnedScore = result.earnedScore ?? 0;
        if (result.totalScore !== undefined) { earnedScore = result.totalEarnedScore; totalScore = result.totalScore; }

        // Tô màu đáp án: chỉ tô khi server đã trả về đáp án đúng (câu bắt buộc đúng mà sai thì không lộ)
        const correctIdList = result.correctOptionIdList || [];
        askBox.querySelectorAll('.vl-opt').forEach(optionEl => {
            const input = optionEl.querySelector('input');
            const id = +input.value;
            input.disabled = true;
            optionEl.classList.toggle('picked', input.checked);
            const stateEl = optionEl.querySelector('.vl-opt-state');
            if (correctIdList.includes(id)) {
                optionEl.classList.add('correct');
                stateEl.className = 'vl-opt-state ph-fill ph-check-circle';
            } else if (input.checked && correctIdList.length) {
                optionEl.classList.add('wrong');
                stateEl.className = 'vl-opt-state ph-fill ph-x-circle';
            }
        });

        const resultEl = askBox.querySelector('.vl-ask-result');
        resultEl.hidden = false;
        // Ba trạng thái khác nhau, không gộp làm hai: "hết lượt nên được đi tiếp" không phải là đúng
        // (không có điểm) nhưng cũng không giống sai-còn-lượt (không phải làm lại nữa).
        const tone = result.isCorrect ? 'ok' : result.isForcedPass ? 'warn' : 'bad';
        const icon = result.isCorrect ? 'ph-check-circle' : result.isForcedPass ? 'ph-warning-circle' : 'ph-x-circle';
        resultEl.className = `vl-ask-result ${tone}`;
        resultEl.innerHTML = `
            <div class="vl-result-line">
                <i class="ph-fill ${icon}"></i>
                <b>${esc(result.message)}</b>
            </div>
            ${result.feedback ? `<div class="vl-result-feedback">${esc(result.feedback)}</div>` : ''}
            ${checkpoint.score > 0 && result.earnedScore !== undefined
                ? `<div class="vl-result-score">Điểm câu này: <b>${vlFormatScore(result.earnedScore)}/${vlFormatScore(checkpoint.score)}</b></div>`
                : ''}`;

        askBox.querySelector('.vl-ask-try').textContent = `Đã trả lời ${checkpoint.tryCount} lần`;
        askBox.querySelector('[data-vl-skip]')?.remove();

        // Cập nhật chip số lượt còn lại ngay tại chỗ (result.remainingAttempt = -1 nghĩa là không giới hạn)
        const triesEl = askBox.querySelector('[data-vl-tries]');
        if (triesEl && result.remainingAttempt >= 0) {
            triesEl.classList.toggle('danger', result.remainingAttempt <= 1);
            triesEl.innerHTML = `<i class="ph ph-arrow-counter-clockwise"></i> Còn ${result.remainingAttempt}/${checkpoint.maxAttempt} lượt`;
        }

        if (result.canContinue) {
            checkpoint.isPassed = true;
            checkpoint.isForcedPass = !!result.isForcedPass;
            renderMarkerList();
            renderProgress();
            submitButton.outerHTML = '<button type="button" class="btn" data-vl-continue><i class="ph-fill ph-play"></i> Xem tiếp</button>';
            askBox.querySelector('[data-vl-continue]').addEventListener('click', () => closeQuestion(true));
            askBox.querySelector('[data-vl-continue]').focus();
        } else {
            submitButton.outerHTML = '<button type="button" class="btn" data-vl-retry><i class="ph ph-arrow-counter-clockwise"></i> Làm lại</button>';
            askBox.querySelector('[data-vl-retry]').addEventListener('click', () => openQuestion(checkpoint));
            askBox.querySelector('[data-vl-retry]').focus();
        }
        // Thẻ vừa cao thêm một khối kết quả — đo lại cho khung video giãn theo, nếu không thì chính phần
        // phản hồi vừa hiện ra lại đẩy nút "Xem tiếp" xuống dưới mép khung.
        fitStageToAsk();
    }

    function closeQuestion(resume) {
        activeCheckpoint = null;
        askBox.hidden = true;
        askBox.innerHTML = '';
        root.classList.remove('vl-blocked');
        root.style.removeProperty('--vl-ask-height');
        renderMarkerList();
        renderProgress();
        setPlayIcon();
        if (resume) video.play().catch(() => {});
    }

    // ---------- Sự kiện của thẻ video ----------

    video.addEventListener('loadedmetadata', () => {
        renderMarkerList();
        renderChapterLayer();
        if (startAt > 0 && startAt < video.duration - 1) {
            const ceiling = seekCeiling();
            video.currentTime = Math.min(startAt, isFinite(ceiling) ? ceiling : startAt);
        }
        renderProgress();
    });

    video.addEventListener('timeupdate', () => {
        // Ghi nhận phần đã xem TRƯỚC khi chặn tua: ở chế độ "bắt buộc xem hết", trần tua chính là mốc đã xem
        // nên nếu chặn trước thì playhead vừa nhích qua trần đã bị kéo ngược lại, video đứng yên tại chỗ.
        trackWatched();
        renderProgress();

        // Chặn tua phải chạy TRƯỚC cả khi đang mở hộp thoại câu hỏi: hộp thoại chỉ che thanh điều khiển,
        // không ngăn được lệnh tua đến từ nơi khác (phím tắt, cử chỉ trên điện thoại, Picture-in-Picture,
        // hay gán thẳng video.currentTime trong DevTools) — bỏ qua bước này là mở đúng đường vòng cần chặn.
        if (guardSeek()) return;
        if (activeCheckpoint) return;   // đang hỏi thì không mở chồng thêm câu hỏi khác

        // Câu hỏi chặn tua: vị trí phát không bao giờ vượt xa mốc (đã bị guardSeek kẹp lại) nên chỉ cần so >=.
        // Câu hỏi không chặn tua: chỉ bật khi playhead vừa đi qua mốc trong lúc phát bình thường — tua vượt
        // qua rồi thì thôi, không bật lại giữa đoạn phim phía sau.
        // blocksSeek() chứ không phải cp.blockSeek: ở bài "cho bấm tới câu hỏi", mốc chặn-tua không còn kẹp
        // playhead lại nữa nên điều kiện >= sẽ khớp với MỌI câu nằm phía trước — bấm tới câu 3 lại bật câu 1.
        const checkpoint = checkpointList.find(cp => !cp.isPassed && !cp.skipped
            && video.currentTime >= cp.atSecond
            && (blocksSeek(cp) || video.currentTime - cp.atSecond <= VL_TRIGGER_WINDOW));

        if (checkpoint) { openQuestion(checkpoint); return; }

        const second = Math.floor(video.currentTime);
        if (onTick && !video.paused && second !== lastTickSecond) {
            lastTickSecond = second;
            onTick(second, Math.floor(maxWatched));
        }
    });

    // Bắt cả seeking lẫn seeked: seeking chặn ngay lúc con trỏ vừa nhảy (kéo thanh, phím mũi tên),
    // seeked chốt lại sau khi trình duyệt đã nhảy xong — có trình duyệt bỏ qua thay đổi currentTime
    // đặt trong seeking nên phải kiểm tra lại lần nữa.
    // Mỗi lần tua đều xóa mẫu vị trí cũ: nhịp timeupdate ngay sau khi tua có bước nhảy lớn, nếu vẫn đem so
    // với mẫu trước đó thì một cú kéo thanh tiến trình cũng bị tính là đã xem xong cả đoạn vừa nhảy qua.
    video.addEventListener('seeking', () => { lastSampleSecond = null; guardSeek(); });
    video.addEventListener('seeked', () => { lastSampleSecond = null; guardSeek(); renderProgress(); });

    // Lấy luôn vị trí hiện tại làm mốc so sánh đầu tiên (không phải null như sau khi tua): vị trí lúc bấm
    // phát là hợp lệ — đã qua guardSeek — nên nhịp timeupdate ngay sau đó phải được tính là đã xem, nếu không
    // thì ở chế độ "bắt buộc xem hết" cứ mỗi lần bấm phát là video lại giật lùi về đúng mốc cũ một nhịp.
    video.addEventListener('play', () => { lastSampleSecond = video.currentTime; setPlayIcon(); });
    video.addEventListener('pause', () => { lastSampleSecond = null; setPlayIcon(); });
    video.addEventListener('progress', renderProgress);
    video.addEventListener('ended', () => {
        // Xem tới hết thì mốc đã xem là trọn thời lượng — không để sót vài giây cuối vì timeupdate cuối cùng
        // bắn trước lúc video dừng hẳn, khiến bài "bắt buộc xem hết" không bao giờ mở khóa hoàn toàn.
        if (isFinite(video.duration)) maxWatched = Math.max(maxWatched, video.duration);
        lastSampleSecond = null;
        setPlayIcon();
        renderProgress();
        if (onTick) onTick(Math.floor(video.duration || 0), Math.floor(maxWatched));
    });
    video.addEventListener('error', () => {
        legendTextEl.innerHTML = '<span class="vl-legend-error"><i class="ph ph-warning"></i> Không tải được video. Kiểm tra lại tệp đính kèm của bài học.</span>';
    });

    // ---------- Thanh điều khiển ----------

    const togglePlay = () => {
        if (activeCheckpoint) return;
        if (video.paused) video.play().catch(() => {}); else video.pause();
    };
    playButton.addEventListener('click', togglePlay);
    bigPlayButton.addEventListener('click', togglePlay);
    video.addEventListener('click', togglePlay);

    // Nút ghi chú chỉ tồn tại khi trang chủ quản thật sự nhận ghi chú (truyền onNoteRequest). Ẩn sẵn trong
    // HTML rồi mới bật lên ở đây, thay vì dựng thẻ có điều kiện: cách này giữ cấu trúc thanh điều khiển
    // giống hệt nhau ở mọi trang, nên CSS thứ tự nút không phải phòng hai kiểu bố cục.
    const noteButton = root.querySelector('.vl-note');
    if (typeof options.onNoteRequest === 'function') {
        noteButton.hidden = false;
        noteButton.addEventListener('click', () => {
            // Dừng hình trước khi mở ô soạn: gõ ghi chú trong lúc video vẫn chạy là vừa gõ vừa bỏ lỡ nội
            // dung — mà chính đoạn đang bỏ lỡ mới là đoạn họ định ghi chú.
            video.pause();
            options.onNoteRequest(Math.floor(video.currentTime || 0));
        });
    }

    root.querySelector('.vl-mute').addEventListener('click', (e) => {
        video.muted = !video.muted;
        e.currentTarget.innerHTML = `<i class="ph-fill ${video.muted ? 'ph-speaker-slash' : 'ph-speaker-high'}"></i>`;
    });

    const RATE_LIST = [1, 1.25, 1.5, 1.75, 2, 0.75];
    root.querySelector('.vl-rate').addEventListener('click', (e) => {
        const next = RATE_LIST[(RATE_LIST.indexOf(video.playbackRate) + 1) % RATE_LIST.length];
        video.playbackRate = next;
        e.currentTarget.textContent = `${next}x`;
    });

    root.querySelector('.vl-full').addEventListener('click', () => {
        if (document.fullscreenElement) document.exitFullscreen();
        else root.requestFullscreen?.().catch(() => {});
    });

    /** Đổi tọa độ chuột trên thanh tiến trình thành mốc thời gian, đã kẹp trong giới hạn cho phép. */
    function seekFromPointer(event) {
        if (activeCheckpoint) return;
        const duration = video.duration;
        if (!isFinite(duration) || duration <= 0) return;

        const rect = track.getBoundingClientRect();
        const ratio = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
        const target = ratio * duration;
        const ceiling = seekCeiling();

        if (target > ceiling) {
            video.currentTime = ceiling;
            notifyBlocked(ceiling);
        } else {
            video.currentTime = target;
        }
        renderProgress();
    }

    let dragging = false;
    track.addEventListener('pointerdown', (event) => {
        dragging = true;
        track.setPointerCapture(event.pointerId);
        seekFromPointer(event);
    });
    track.addEventListener('pointermove', (event) => { if (dragging) seekFromPointer(event); });
    track.addEventListener('pointerup', (event) => {
        dragging = false;
        track.releasePointerCapture?.(event.pointerId);
    });
    track.addEventListener('pointercancel', () => { dragging = false; });

    track.addEventListener('keydown', (event) => {
        if (activeCheckpoint) return;
        const step = event.key === 'ArrowRight' ? 5 : event.key === 'ArrowLeft' ? -5 : 0;
        if (!step) return;
        event.preventDefault();
        const ceiling = seekCeiling();
        const target = video.currentTime + step;
        if (target > ceiling) { notifyBlocked(ceiling); video.currentTime = ceiling; }
        else video.currentTime = Math.max(0, target);
    });

    renderProgress();
    renderScore();
    // Vẽ thẳng một lần thay vì chờ renderMarkerList: hàm đó thoát sớm khi chưa biết thời lượng video, mà
    // danh sách câu hỏi thì không cần thời lượng — học viên phải thấy được bài này hỏi gì ngay từ lúc mở,
    // kể cả khi tệp video hỏng hoặc mạng chậm chưa tải nổi metadata.
    renderQuestionList();
    setPlayIcon();

    return {
        video,
        get currentSecond() { return Math.floor(video.currentTime || 0); },
        get maxWatchedSecond() { return Math.floor(maxWatched); },

        /** Tua tới một giây bất kỳ rồi phát tiếp. Dùng cho danh sách ghi chú theo mốc thời gian: bấm vào
         *  ghi chú là về đúng đoạn video đã ghi.
         *
         *  Vẫn đi qua đúng luật chặn tua như mọi nút khác trong trình phát — ghi chú không phải cửa sau để
         *  nhảy qua câu hỏi bắt buộc, và người học bấm một ghi chú ở đoạn chưa mở khóa sẽ nhận đúng lý do
         *  vì sao chưa tới được. */
        jumpToSecond,

        /** Trỏ danh sách câu hỏi sang một ô DOM khác rồi vẽ lại ngay. Dùng khi trang chủ quản vẽ lại vùng
         *  chứa danh sách (VD cây nội dung vẽ lại sau mỗi lần đóng/mở nhánh): ô cũ lúc đó đã lìa khỏi DOM,
         *  không trỏ lại thì danh sách câu hỏi của bài đang học biến mất mà không có lỗi nào báo ra. */
        remountQuestionList(element) {
            if (destroyed || !element || element === questionListBox) return;
            questionListBox = element;
            questionListStamp = '';   // xóa dấu vân để renderQuestionList không bỏ qua vì "không có gì đổi"
            renderQuestionList();
        },

        destroy() {
            if (destroyed) return;
            destroyed = true;
            video.pause();
            video.removeAttribute('src');
            video.load();
            container.innerHTML = '';
            // Danh sách câu hỏi có thể nằm ngoài container của trình phát (dưới nội dung bài học) nên
            // container.innerHTML = '' không dọn được — phải xóa riêng, không thì chuyển sang bài học khác
            // vẫn còn nguyên danh sách câu hỏi của bài cũ nằm dưới.
            if (questionListBox) questionListBox.innerHTML = '';
        }
    };
}
