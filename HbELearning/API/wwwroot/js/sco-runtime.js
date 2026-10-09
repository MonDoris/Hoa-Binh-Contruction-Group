// File: wwwroot/js/sco-runtime.js
/**
 * Phía SCO của các trang nội dung do hệ thống phục vụ — bài học (sco-lesson.html), bài kiểm tra (sco-quiz.html),
 * mô phỏng 3D (simulation.html?sco=1) — khi chạy trong trình phát SCORM của khóa học.
 *
 * Đúng như mọi SCO SCORM 2004 của bất kỳ nhà cung cấp nào: tìm bộ API "API_1484_11" theo thuật toán của ADL (đi
 * ngược lên các cửa sổ cha, rồi cửa sổ đã mở ra nó), Initialize khi trang mở, ghi mô hình dữ liệu cmi.*, xin điều
 * hướng qua adl.nav.request và Terminate khi trang bị gỡ. Trình phát không phân biệt SCO "nhà" với gói nhập ngoài.
 *
 * Kết quả THẬT vẫn do máy chủ chấm (tiến độ bài học, bài làm, lượt mô phỏng); thứ trang báo qua cmi.* chỉ để trình
 * phát cập nhật mục lục và điều hướng ngay — máy chủ đối chiếu lại với dữ liệu của nó ở mỗi lần Commit.
 *
 * Mở trang ngoài trình phát (không thấy API) thì mọi lệnh thành không-làm-gì: trang vẫn dùng được như trang thường.
 */
const ScoRuntime = (function () {
    /** Thuật toán tìm API của ADL (SCORM 2004 RTE, phụ lục): tối đa 500 cấp cửa sổ cha. */
    function findApi(start) {
        let win = start;
        for (let depth = 0; win && depth < 500; depth++) {
            try {
                if (win.API_1484_11) return win.API_1484_11;
                if (!win.parent || win.parent === win) break;
                win = win.parent;
            } catch {
                break; // khác nguồn gốc — không với tới được nữa
            }
        }
        return null;
    }

    let api = null;
    try { api = findApi(window) || (window.opener ? findApi(window.opener) : null); } catch { api = null; }

    const params = new URLSearchParams(location.search);
    /** Ngữ cảnh trình phát truyền theo địa chỉ SCO: lượt học của học viên, hoặc khóa học đang xem thử. */
    const context = {
        enrollmentId: parseInt(params.get('enrollmentId') || '0', 10) || null,
        preview: params.get('preview') === '1',
        courseId: parseInt(params.get('courseId') || '0', 10) || null,
    };

    let phase = 'idle';            // idle → running → terminated
    const leaveHandlerList = [];

    function call(name, ...args) {
        if (!api) return '';
        try { return String(api[name](...args)); } catch { return ''; }
    }

    function initialize() {
        if (!api) return false;
        if (phase === 'running') return true;
        if (phase !== 'idle') return false;
        const ok = call('Initialize', '') === 'true';
        phase = ok ? 'running' : 'failed';
        return ok;
    }

    const get = (element) => phase === 'running' ? call('GetValue', element) : '';
    const set = (element, value) => phase === 'running' && call('SetValue', element, String(value)) === 'true';
    const commit = () => { if (phase === 'running') call('Commit', ''); };

    /** Kết thúc SCO, tùy chọn xin điều hướng (continue, previous, exit, {target=ID}choice...). */
    function terminate(navRequest) {
        if (phase !== 'running') return;
        if (navRequest) set('adl.nav.request', navRequest);
        phase = 'terminated';
        call('Terminate', '');
    }

    /** adl.nav.request_valid.continue / previous — trình phát trả lời theo đúng sequencing đang chạy. */
    const navValid = (request) => get(`adl.nav.request_valid.${request}`) === 'true';

    /** Báo hoàn thành (và đạt / trượt nếu có) rồi Commit ngay để mục lục cập nhật. */
    function reportResult({ completed = true, passed = null, scaled = null, raw = null, min = null, max = null, progress = null } = {}) {
        if (phase !== 'running') return;
        if (scaled != null) set('cmi.score.scaled', Math.max(-1, Math.min(1, Number(scaled))).toFixed(4));
        if (raw != null) set('cmi.score.raw', Number(raw));
        if (min != null) set('cmi.score.min', Number(min));
        if (max != null) set('cmi.score.max', Number(max));
        if (progress != null) set('cmi.progress_measure', Math.max(0, Math.min(1, Number(progress))).toFixed(4));
        set('cmi.completion_status', completed ? 'completed' : 'incomplete');
        if (passed != null) set('cmi.success_status', passed ? 'passed' : 'failed');
        commit();
    }

    /** Ghi một câu trả lời vào cmi.interactions (RTE 4.2.9) — báo cáo của khóa học xem lại được từng câu. */
    function addInteraction({ id, type = 'choice', description, learnerResponse, correctResponse, result, weighting, latencySecond }) {
        if (phase !== 'running') return;
        const index = parseInt(get('cmi.interactions._count') || '0', 10) || 0;
        const prefix = `cmi.interactions.${index}.`;
        if (!set(prefix + 'id', id)) return;
        set(prefix + 'type', type);
        if (description) set(prefix + 'description', String(description).slice(0, 250));
        if (weighting != null) set(prefix + 'weighting', weighting);
        if (correctResponse != null) set(prefix + 'correct_responses.0.pattern', correctResponse);
        if (learnerResponse != null) set(prefix + 'learner_response', learnerResponse);
        if (result) set(prefix + 'result', result);
        if (latencySecond != null) set(prefix + 'latency', `PT${Math.max(0, Math.round(latencySecond))}S`);
        set(prefix + 'timestamp', new Date().toISOString().slice(0, 19));
    }

    /** Trình phát gỡ trang (bấm Tiếp / chọn mục lục / đóng tab): SCO phải tự Terminate ngay trong sự kiện gỡ trang
     *  — trình phát chờ đúng lệnh đó để chốt dữ liệu rồi mới giao mục kế tiếp. Việc dọn dẹp của trang (lưu vị trí
     *  video...) chạy trước. */
    let left = false;
    function onLeave() {
        if (left) return;
        left = true;
        for (const handler of leaveHandlerList) { try { handler(); } catch { /* không để một lỗi chặn Terminate */ } }
        terminate(null);
    }
    window.addEventListener('pagehide', onLeave);
    window.addEventListener('beforeunload', onLeave);

    return {
        context,
        /** Đang chạy trong trình phát (có bộ API của LMS). */
        get isTracked() { return !!api; },
        get phase() { return phase; },
        initialize, get, set, commit, terminate, navValid, reportResult, addInteraction,
        onLeave: (handler) => leaveHandlerList.push(handler),
        /** Xin trình phát đi tiếp mục sau. KHÔNG hỏi adl.nav.request_valid trước: trình phát trả lời câu đó theo lần
         *  đồng bộ gần nhất, mà kết quả SCO vừa báo (VD vừa hoàn thành bài) có thể chưa kịp về máy chủ — hỏi lúc này là
         *  nhận câu trả lời cũ. Gửi thẳng "continue": trình phát xét theo sequencing mới nhất, không đi được thì tự
         *  báo lý do và mở mục lục. */
        continueNext() { terminate('continue'); },
    };
})();
