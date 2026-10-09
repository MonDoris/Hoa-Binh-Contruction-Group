// File: wwwroot/js/scorm-rte.js
// Môi trường chạy (Run-Time Environment) SCORM phía trình duyệt: hai bộ API mà nội dung SCORM tìm thấy trên cửa
// sổ trình phát — API_1484_11 (SCORM 2004 4th Edition) và API (SCORM 1.2).
//
// Chuẩn bắt các hàm API trả lời ĐỒNG BỘ (SCO gọi GetValue rồi dùng kết quả ngay ở dòng sau), nên toàn bộ mô hình
// dữ liệu CMI sống trong bộ nhớ ở đây và mọi luật của chuẩn — quyền đọc/ghi, kiểu dữ liệu, khoảng giá trị, thứ tự
// phụ thuộc, mã lỗi — được áp NGAY tại chỗ. Máy chủ chỉ nhận ảnh chụp dữ liệu ở mỗi lần Commit / Terminate.
//
// Tham chiếu: SCORM 2004 4th Edition RTE mục 3 (API) và mục 4 (Data Model); SCORM 1.2 RTE mục 3.3 và 3.4.
(function (global) {
    'use strict';

    // ==================== TIỆN ÍCH CHUNG ====================

    const REAL_RE = /^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?$/;
    const ISO_DURATION_RE = /^P(?:(\d+(?:\.\d+)?)Y)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)D)?(?:T(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d{1,2})?)S)?)?$/;
    const ISO_TIME_RE = /^(\d{4})(?:-(\d{2})(?:-(\d{2})(?:T(\d{2})(?::(\d{2})(?::(\d{2})(?:\.(\d{1,2}))?)?)?(Z|[+-]\d{2}(?::?\d{2})?)?)?)?)?$/;
    const LANG_RE = /^([a-zA-Z]{2,3}|i|x)(-[a-zA-Z0-9]{1,8})*$/;

    function isReal(value) { return REAL_RE.test(value); }

    /** Thời lượng ISO 8601 (timeinterval) → giây; null nếu sai định dạng. */
    function parseIsoDuration(value) {
        if (typeof value !== 'string' || value === 'P' || value.endsWith('T')) return null;
        const match = ISO_DURATION_RE.exec(value);
        if (!match) return null;
        const [, y, mo, d, h, mi, s] = match.map(x => (x === undefined ? 0 : parseFloat(x)));
        return y * 31536000 + mo * 2592000 + d * 86400 + h * 3600 + mi * 60 + s;
    }

    /** Giây → thời lượng ISO 8601 dạng PT#H#M#S (độ chính xác 1/100 giây như chuẩn yêu cầu). */
    function formatIsoDuration(second) {
        let cs = Math.max(0, Math.round((second || 0) * 100));
        const h = Math.floor(cs / 360000); cs -= h * 360000;
        const m = Math.floor(cs / 6000); cs -= m * 6000;
        const s = cs / 100;
        return `PT${h}H${m}M${Number.isInteger(s) ? s : s.toFixed(2)}S`;
    }

    function isIsoTime(value) {
        const match = ISO_TIME_RE.exec(value);
        if (!match) return false;
        const year = +match[1];
        if (year < 1970 || year > 2038) return false;
        const month = match[2] !== undefined ? +match[2] : 1, day = match[3] !== undefined ? +match[3] : 1;
        if (month < 1 || month > 12 || day < 1 || day > 31) return false;
        if (match[4] !== undefined && +match[4] > 23) return false;
        if (match[5] !== undefined && +match[5] > 59) return false;
        if (match[6] !== undefined && +match[6] > 59) return false;
        return true;
    }

    /** CMITimespan của 1.2: HHHH:MM:SS(.SS) → giây; null nếu sai. */
    function parseCmiTimespan(value) {
        const match = /^(\d{2,4}):(\d{2}):(\d{2})(\.\d{1,2})?$/.exec(value);
        if (!match || +match[2] > 59 || +match[3] > 59) return null;
        return +match[1] * 3600 + +match[2] * 60 + +match[3] + (match[4] ? parseFloat(match[4]) : 0);
    }

    function formatCmiTimespan(second) {
        let cs = Math.max(0, Math.round((second || 0) * 100));
        const h = Math.floor(cs / 360000); cs -= h * 360000;
        const m = Math.floor(cs / 6000); cs -= m * 6000;
        const s = Math.floor(cs / 100), frac = cs % 100;
        const pad = (n, w) => String(n).padStart(w, '0');
        return `${pad(Math.min(h, 9999), 4)}:${pad(m, 2)}:${pad(s, 2)}${frac ? '.' + pad(frac, 2) : ''}`;
    }

    function toNumber(value) {
        if (value === undefined || value === null || value === '') return null;
        const number = parseFloat(value);
        return Number.isFinite(number) ? number : null;
    }

    /** Bỏ tiền tố {lang=xx} và kiểm mã ngôn ngữ của localized_string_type. */
    function stripLang(value) {
        const match = /^\{lang=([^}]*)\}/.exec(value);
        if (!match) return { ok: true, text: value };
        return { ok: match[1] === '' || LANG_RE.test(match[1]), text: value.slice(match[0].length) };
    }

    // ==================== SCORM 2004 4th EDITION ====================

    const ERROR_2004 = {
        0: 'No Error', 101: 'General Exception', 102: 'General Initialization Failure', 103: 'Already Initialized',
        104: 'Content Instance Terminated', 111: 'General Termination Failure', 112: 'Termination Before Initialization',
        113: 'Termination After Termination', 122: 'Retrieve Data Before Initialization', 123: 'Retrieve Data After Termination',
        132: 'Store Data Before Initialization', 133: 'Store Data After Termination', 142: 'Commit Before Initialization',
        143: 'Commit After Termination', 201: 'General Argument Error', 301: 'General Get Failure', 351: 'General Set Failure',
        391: 'General Commit Failure', 401: 'Undefined Data Model Element', 402: 'Unimplemented Data Model Element',
        403: 'Data Model Element Value Not Initialized', 404: 'Data Model Element Is Read Only',
        405: 'Data Model Element Is Write Only', 406: 'Data Model Element Type Mismatch',
        407: 'Data Model Element Value Out Of Range', 408: 'Data Model Dependency Not Established',
    };

    const COMPLETION_VOCAB = ['completed', 'incomplete', 'not attempted', 'unknown'];
    const SUCCESS_VOCAB = ['passed', 'failed', 'unknown'];
    const EXIT_VOCAB = ['time-out', 'suspend', 'logout', 'normal', ''];
    const INTERACTION_TYPE_VOCAB = ['true-false', 'choice', 'fill-in', 'long-fill-in', 'likert', 'matching',
        'performance', 'sequencing', 'numeric', 'other'];
    const RESULT_VOCAB = ['correct', 'incorrect', 'unanticipated', 'neutral'];
    const NAV_VOCAB = ['continue', 'previous', 'exit', 'exitAll', 'abandon', 'abandonAll', 'suspendAll', '_none_'];
    const TIME_LIMIT_VOCAB = ['exit,message', 'continue,message', 'exit,no message', 'continue,no message'];

    // Bộ kiểm giá trị: trả 0 (hợp lệ), 406 (sai kiểu) hoặc 407 (ngoài khoảng).
    const V = {
        vocab: list => value => (list.includes(value) ? 0 : 406),
        real: (min, max) => value => {
            if (!isReal(value)) return 406;
            const number = parseFloat(value);
            if ((min !== undefined && number < min) || (max !== undefined && number > max)) return 407;
            return 0;
        },
        // characterstring: chuẩn chỉ BẮT LMS nhận tối thiểu SPM ký tự. Nhận rộng gấp bốn để nội dung lỡ ghi dài
        // hơn một chút vẫn chạy, nhưng chặn chuỗi khổng lồ làm phình dữ liệu.
        chars: spm => value => (value.length <= spm * 4 ? 0 : 406),
        localized: spm => value => {
            const parsed = stripLang(value);
            if (!parsed.ok) return 406;
            return parsed.text.length <= spm * 4 ? 0 : 406;
        },
        longId: value => (value.length > 0 && value.length <= 4000 && !/^\s|\s$/.test(value) ? 0 : 406),
        time: value => (isIsoTime(value) ? 0 : 406),
        interval: value => (parseIsoDuration(value) === null ? 406 : 0),
        language: value => (value === '' || LANG_RE.test(value) ? 0 : 406),
        nav: value => (NAV_VOCAB.includes(value) || /^\{target=[^}]+\}(choice|jump)$/.test(value) ? 0 : 406),
        captioning: value => (['-1', '0', '1'].includes(value) ? 0 : 406),
    };

    /** Phần tử đơn của cmi.* / adl.* — access: r / w / rw. */
    const SCALAR_2004 = {
        'cmi._version': { access: 'r' },
        'cmi.completion_status': { access: 'rw', check: V.vocab(COMPLETION_VOCAB) },
        'cmi.completion_threshold': { access: 'r' },
        'cmi.credit': { access: 'r' },
        'cmi.entry': { access: 'r' },
        'cmi.exit': { access: 'w', check: V.vocab(EXIT_VOCAB) },
        'cmi.launch_data': { access: 'r' },
        'cmi.learner_id': { access: 'r' },
        'cmi.learner_name': { access: 'r' },
        'cmi.learner_preference.audio_level': { access: 'rw', check: V.real(0) },
        'cmi.learner_preference.language': { access: 'rw', check: V.language },
        'cmi.learner_preference.delivery_speed': { access: 'rw', check: V.real(0) },
        'cmi.learner_preference.audio_captioning': { access: 'rw', check: V.captioning },
        'cmi.location': { access: 'rw', check: V.chars(1000) },
        'cmi.max_time_allowed': { access: 'r' },
        'cmi.mode': { access: 'r' },
        'cmi.progress_measure': { access: 'rw', check: V.real(0, 1) },
        'cmi.scaled_passing_score': { access: 'r' },
        'cmi.score.scaled': { access: 'rw', check: V.real(-1, 1) },
        'cmi.score.raw': { access: 'rw', check: V.real() },
        'cmi.score.min': { access: 'rw', check: V.real() },
        'cmi.score.max': { access: 'rw', check: V.real() },
        'cmi.session_time': { access: 'w', check: V.interval },
        'cmi.success_status': { access: 'rw', check: V.vocab(SUCCESS_VOCAB) },
        'cmi.suspend_data': { access: 'rw', check: V.chars(64000) },
        'cmi.time_limit_action': { access: 'r' },
        'cmi.total_time': { access: 'r' },
        'adl.nav.request': { access: 'rw', check: V.nav },
    };

    const CHILDREN_2004 = {
        'cmi.comments_from_learner': 'comment,location,timestamp',
        'cmi.comments_from_lms': 'comment,location,timestamp',
        'cmi.interactions': 'id,type,objectives,timestamp,correct_responses,weighting,learner_response,result,latency,description',
        'cmi.learner_preference': 'audio_level,language,delivery_speed,audio_captioning',
        'cmi.objectives': 'id,score,success_status,completion_status,progress_measure,description',
        'cmi.score': 'scaled,raw,min,max',
        'adl.data': 'id,store',
    };

    const INTERACTION_FIELD = {
        id: V.longId,
        type: V.vocab(INTERACTION_TYPE_VOCAB),
        timestamp: V.time,
        weighting: V.real(),
        latency: V.interval,
        description: V.localized(250),
        // learner_response / result kiểm riêng theo loại câu hỏi
    };

    const OBJECTIVE_FIELD = {
        id: V.longId,
        'score.scaled': V.real(-1, 1),
        'score.raw': V.real(),
        'score.min': V.real(),
        'score.max': V.real(),
        success_status: V.vocab(SUCCESS_VOCAB),
        completion_status: V.vocab(COMPLETION_VOCAB),
        progress_measure: V.real(0, 1),
        description: V.localized(250),
    };

    const COMMENT_FIELD = {
        comment: V.localized(4000),
        location: V.chars(250),
        timestamp: V.time,
    };

    // ----- Định dạng câu trả lời của interactions (RTE 4.2.9.1) -----

    const SEP = '[,]';
    const isShortId = s => s.length > 0 && s.length <= 250 && !/\s/.test(s) && !s.includes('[') && !s.includes(']');

    function stripDelimiter(value, nameList) {
        // Các tiền tố {case_matters=true}{order_matters=false} ở đầu mẫu đúng — bóc ra rồi kiểm phần còn lại.
        let rest = value;
        for (;;) {
            const match = /^\{(case_matters|order_matters|lang)=([^}]*)\}/.exec(rest);
            if (!match || !nameList.includes(match[1])) break;
            if (match[1] !== 'lang' && !['true', 'false'].includes(match[2])) return null;
            if (match[1] === 'lang' && match[2] !== '' && !LANG_RE.test(match[2])) return null;
            rest = rest.slice(match[0].length);
        }
        return rest;
    }

    function checkResponse(type, value, isPattern) {
        switch (type) {
            case 'true-false':
                return value === 'true' || value === 'false';
            case 'choice': {
                if (value === '') return true;
                const list = value.split(SEP);
                return list.every(isShortId) && new Set(list).size === list.length;
            }
            case 'fill-in': {
                const rest = isPattern ? stripDelimiter(value, ['case_matters', 'order_matters']) : value;
                if (rest === null) return false;
                return rest.split(SEP).every(part => { const p = stripLang(part); return p.ok && p.text.length <= 250 * 4; });
            }
            case 'long-fill-in': {
                const rest = isPattern ? stripDelimiter(value, ['case_matters']) : value;
                if (rest === null) return false;
                const p = stripLang(rest);
                return p.ok && p.text.length <= 4000 * 4;
            }
            case 'likert':
                return isShortId(value);
            case 'matching':
                return value.split(SEP).every(pair => { const x = pair.split('[.]'); return x.length === 2 && isShortId(x[0]) && isShortId(x[1]); });
            case 'performance': {
                const rest = isPattern ? stripDelimiter(value, ['order_matters']) : value;
                if (rest === null) return false;
                return rest.split(SEP).every(step => {
                    const x = step.split('[.]');
                    if (x.length !== 2 || (x[0] === '' && x[1] === '')) return false;
                    if (x[0] !== '' && !isShortId(x[0])) return false;
                    if (x[1] === '') return true;
                    if (isPattern && x[1].includes('[:]')) {
                        const [a, b] = x[1].split('[:]');
                        return (a === '' || isReal(a)) && (b === '' || isReal(b));
                    }
                    return x[1].length <= 250;
                });
            }
            case 'sequencing':
                return value.split(SEP).every(isShortId);
            case 'numeric':
                if (!isPattern) return isReal(value);
                if (value.includes('[:]')) {
                    const [a, b] = value.split('[:]');
                    if (!((a === '' || isReal(a)) && (b === '' || isReal(b)))) return false;
                    return a === '' || b === '' || parseFloat(a) <= parseFloat(b);
                }
                return isReal(value);
            case 'other':
                return value.length <= 4000 * 4;
            default:
                return false;
        }
    }

    /** Số mẫu đúng tối đa của từng loại câu hỏi (RTE bảng 4.2.9.1a). */
    function maxPatternCount(type) {
        return ['true-false', 'likert', 'numeric', 'other'].includes(type) ? 1 : 10000;
    }

    /**
     * Bộ API SCORM 2004 (API_1484_11).
     * @param {object} options
     * @param {object} options.init   ScormCmiInitDto từ máy chủ.
     * @param {object|null} options.saved  Ảnh chụp dữ liệu CMI đã lưu (học tiếp); null = lượt thử mới.
     * @param {(kind:string, target?:string)=>string} options.navValid  Trả "true"/"false"/"unknown".
     * @param {(payload:object)=>void} options.onCommit
     * @param {(payload:object, navRequest:string|null)=>void} options.onTerminate
     */
    function Scorm2004Api(options) {
        const init = options.init;
        let phase = 'idle';            // idle → running → terminated
        let lastError = 0, lastDiagnostic = '';
        let initializedAt = 0;

        const value = {
            'cmi.completion_status': 'unknown',
            'cmi.success_status': 'unknown',
            'cmi.learner_preference.audio_level': '1',
            'cmi.learner_preference.language': '',
            'cmi.learner_preference.delivery_speed': '1',
            'cmi.learner_preference.audio_captioning': '0',
            'adl.nav.request': '_none_',
        };
        const interactionList = [];
        const objectiveList = [];
        const commentList = [];
        const sharedList = (init.sharedDataList || []).map(x => ({ id: x.id, store: x.store || '', readable: x.readable, writable: x.writable, dirty: false, known: (x.store || '') !== '' }));

        // ----- Nạp dữ liệu đã lưu (học tiếp) -----
        const saved = options.saved || null;
        if (saved) {
            for (const [key, raw] of Object.entries(saved)) {
                const text = String(raw);
                let match;
                if ((match = /^cmi\.interactions\.(\d+)\.(.+)$/.exec(key))) {
                    const record = interactionList[+match[1]] || (interactionList[+match[1]] = { objectives: [], correct_responses: [] });
                    let sub;
                    if ((sub = /^objectives\.(\d+)\.id$/.exec(match[2]))) record.objectives[+sub[1]] = text;
                    else if ((sub = /^correct_responses\.(\d+)\.pattern$/.exec(match[2]))) record.correct_responses[+sub[1]] = text;
                    else record[match[2]] = text;
                } else if ((match = /^cmi\.objectives\.(\d+)\.(.+)$/.exec(key))) {
                    (objectiveList[+match[1]] || (objectiveList[+match[1]] = {}))[match[2]] = text;
                } else if ((match = /^cmi\.comments_from_learner\.(\d+)\.(.+)$/.exec(key))) {
                    (commentList[+match[1]] || (commentList[+match[1]] = {}))[match[2]] = text;
                } else if (SCALAR_2004[key] && SCALAR_2004[key].access !== 'r' && key !== 'cmi.session_time' && key !== 'adl.nav.request') {
                    value[key] = text;
                }
            }
            // Mảng có lỗ (dữ liệu hỏng) thì bỏ phần từ chỗ lỗ trở đi — _count phải khớp với chỉ số liên tục.
            [interactionList, objectiveList, commentList].forEach(list => {
                const hole = list.findIndex(x => !x);
                if (hole >= 0) list.length = hole;
            });
            interactionList.forEach(x => { x.objectives = x.objectives.filter(v => v !== undefined); x.correct_responses = x.correct_responses.filter(v => v !== undefined); });
        }

        // Mục tiêu khai trong sequencing: LMS phải khởi tạo sẵn (kèm trạng thái đọc từ mục tiêu toàn cục).
        (init.objectiveList || []).forEach(o => {
            let record = objectiveList.find(x => x.id === o.id);
            if (!record) { record = { id: o.id }; objectiveList.push(record); }
            const setIfKnown = (field, v, unknownValue) => {
                if (v === null || v === undefined || v === unknownValue) return;
                if (record[field] === undefined || record[field] === '' || record[field] === unknownValue) record[field] = String(v);
            };
            setIfKnown('success_status', o.successStatus, 'unknown');
            setIfKnown('completion_status', o.completionStatus, 'unknown');
            setIfKnown('score.scaled', o.scoreScaled);
            setIfKnown('score.raw', o.scoreRaw);
            setIfKnown('score.min', o.scoreMin);
            setIfKnown('score.max', o.scoreMax);
            setIfKnown('progress_measure', o.progressMeasure);
        });

        // Giá trị chỉ đọc do LMS cấp.
        const readOnly = {
            'cmi._version': '1.0',
            'cmi.credit': init.credit,
            'cmi.entry': init.entry,
            'cmi.mode': init.mode,
            'cmi.learner_id': init.learnerId,
            'cmi.learner_name': init.learnerName,
            'cmi.total_time': formatIsoDuration(init.totalTimeSecond || 0),
            'cmi.time_limit_action': TIME_LIMIT_VOCAB.includes(init.timeLimitAction) ? init.timeLimitAction : 'continue,no message',
        };
        if (init.launchData !== null && init.launchData !== undefined && init.launchData !== '') readOnly['cmi.launch_data'] = init.launchData;
        if (init.completionThreshold !== null && init.completionThreshold !== undefined) readOnly['cmi.completion_threshold'] = String(init.completionThreshold);
        if (init.scaledPassingScore !== null && init.scaledPassingScore !== undefined) readOnly['cmi.scaled_passing_score'] = String(init.scaledPassingScore);
        if (init.maxTimeAllowed && parseIsoDuration(init.maxTimeAllowed) !== null) readOnly['cmi.max_time_allowed'] = init.maxTimeAllowed;

        // ----- Trạng thái đánh giá theo ngưỡng (RTE 4.2.4.1 / 4.2.22.1) -----
        function evaluatedCompletion() {
            const threshold = toNumber(readOnly['cmi.completion_threshold']);
            if (threshold !== null) {
                const progress = toNumber(value['cmi.progress_measure']);
                if (progress === null) return 'unknown';
                return progress >= threshold ? 'completed' : 'incomplete';
            }
            return value['cmi.completion_status'];
        }
        function evaluatedSuccess() {
            const passing = toNumber(readOnly['cmi.scaled_passing_score']);
            if (passing !== null) {
                const scaled = toNumber(value['cmi.score.scaled']);
                if (scaled === null) return 'unknown';
                return scaled >= passing ? 'passed' : 'failed';
            }
            return value['cmi.success_status'];
        }

        function fail(code, diagnostic) { lastError = code; lastDiagnostic = diagnostic || ERROR_2004[code] || ''; return code; }

        // ----- GetValue -----
        function getValue(element) {
            if (phase === 'idle') { fail(122); return ''; }
            if (phase === 'terminated') { options.onLateCall?.('GetValue', element); fail(123); return ''; }
            if (typeof element !== 'string' || element === '') { fail(301, 'Tên phần tử rỗng.'); return ''; }
            lastError = 0; lastDiagnostic = '';
            const element_ = element;
            let match;

            // _children / _count / _version
            if (element_.endsWith('._children')) {
                const base = element_.slice(0, -10);
                if (CHILDREN_2004[base]) return CHILDREN_2004[base];
                if ((match = /^cmi\.objectives\.(\d+)\.score$/.exec(base))) {
                    if (+match[1] >= objectiveList.length) { fail(301, 'Mục tiêu chưa tồn tại.'); return ''; }
                    return 'scaled,raw,min,max';
                }
                if (isKnownElement(base)) { fail(301, `${base} không có phần tử con.`); return ''; }
                fail(401); return '';
            }
            if (element_.endsWith('._count')) {
                const base = element_.slice(0, -7);
                if (base === 'cmi.interactions') return String(interactionList.length);
                if (base === 'cmi.objectives') return String(objectiveList.length);
                if (base === 'cmi.comments_from_learner') return String(commentList.length);
                if (base === 'cmi.comments_from_lms') return '0';
                if (base === 'adl.data') return String(sharedList.length);
                if ((match = /^cmi\.interactions\.(\d+)\.(objectives|correct_responses)$/.exec(base))) {
                    const record = interactionList[+match[1]];
                    if (!record) { fail(301, 'Interaction chưa tồn tại.'); return ''; }
                    return String(record[match[2]].length);
                }
                if (isKnownElement(base)) { fail(301, `${base} không phải mảng.`); return ''; }
                fail(401); return '';
            }

            // adl.nav.request_valid.*
            if (element_ === 'adl.nav.request_valid.continue' || element_ === 'adl.nav.request_valid.previous')
                return options.navValid(element_.slice(23)) || 'unknown';
            if ((match = /^adl\.nav\.request_valid\.(choice|jump)\.\{target=([^}]+)\}$/.exec(element_)))
                return options.navValid(match[1], match[2]) || 'unknown';
            if (element_.startsWith('adl.nav.request_valid.')) { fail(401); return ''; }

            // Phần tử đơn
            if (SCALAR_2004[element_]) {
                const def = SCALAR_2004[element_];
                if (def.access === 'w') { fail(405); return ''; }
                if (element_ === 'cmi.completion_status') return evaluatedCompletion();
                if (element_ === 'cmi.success_status') return evaluatedSuccess();
                if (def.access === 'r') {
                    if (readOnly[element_] === undefined) { fail(403); return ''; }
                    return readOnly[element_];
                }
                if (value[element_] === undefined) { fail(403); return ''; }
                return value[element_];
            }

            // Mảng
            if ((match = /^cmi\.interactions\.(\d+)\.(.+)$/.exec(element_))) {
                const record = interactionList[+match[1]];
                const field = match[2];
                if (!isInteractionField(field)) { fail(401); return ''; }
                if (!record) { fail(301, 'Interaction chưa tồn tại.'); return ''; }
                let sub;
                if ((sub = /^objectives\.(\d+)\.id$/.exec(field))) {
                    if (+sub[1] >= record.objectives.length) { fail(301); return ''; }
                    return record.objectives[+sub[1]];
                }
                if ((sub = /^correct_responses\.(\d+)\.pattern$/.exec(field))) {
                    if (+sub[1] >= record.correct_responses.length) { fail(301); return ''; }
                    return record.correct_responses[+sub[1]];
                }
                if (record[field] === undefined) { fail(403); return ''; }
                return record[field];
            }
            if ((match = /^cmi\.objectives\.(\d+)\.(.+)$/.exec(element_))) {
                const field = match[2];
                if (!(field in OBJECTIVE_FIELD)) { fail(401); return ''; }
                const record = objectiveList[+match[1]];
                if (!record) { fail(301, 'Mục tiêu chưa tồn tại.'); return ''; }
                if (field === 'success_status' || field === 'completion_status') return record[field] || 'unknown';
                if (record[field] === undefined) { fail(403); return ''; }
                return record[field];
            }
            if ((match = /^cmi\.comments_from_learner\.(\d+)\.(.+)$/.exec(element_))) {
                if (!(match[2] in COMMENT_FIELD)) { fail(401); return ''; }
                const record = commentList[+match[1]];
                if (!record) { fail(301); return ''; }
                if (record[match[2]] === undefined) { fail(403); return ''; }
                return record[match[2]];
            }
            if ((match = /^cmi\.comments_from_lms\.(\d+)\.(.+)$/.exec(element_))) {
                if (!(match[2] in COMMENT_FIELD)) { fail(401); return ''; }
                fail(301); return '';
            }
            if ((match = /^adl\.data\.(\d+)\.(id|store)$/.exec(element_))) {
                const record = sharedList[+match[1]];
                if (!record) { fail(301); return ''; }
                if (match[2] === 'id') return record.id;
                if (!record.readable) { fail(405); return ''; }
                if (!record.known) { fail(403); return ''; }
                return record.store;
            }

            fail(401);
            return '';
        }

        function isKnownElement(name) {
            return !!SCALAR_2004[name] || /^cmi\.(interactions|objectives|comments_from_learner|comments_from_lms)\.\d+\.[a-z_.]+$/.test(name)
                || /^adl\.data\.\d+\.(id|store)$/.test(name);
        }

        function isInteractionField(field) {
            return field in INTERACTION_FIELD || field === 'learner_response' || field === 'result'
                || /^objectives\.\d+\.id$/.test(field) || /^correct_responses\.\d+\.pattern$/.test(field);
        }

        // ----- SetValue -----
        function setValue(element, raw) {
            if (phase === 'idle') { fail(132); return 'false'; }
            if (phase === 'terminated') { options.onLateCall?.('SetValue', element); fail(133); return 'false'; }
            if (typeof element !== 'string' || element === '') { fail(351, 'Tên phần tử rỗng.'); return 'false'; }
            const text = raw === undefined || raw === null ? '' : String(raw);
            lastError = 0; lastDiagnostic = '';
            const code = doSet(element, text);
            if (code) { fail(code); return 'false'; }
            return 'true';
        }

        function doSet(element, text) {
            let match;
            if (/\._(children|count|version)$/.test(element) || element === 'cmi._version') {
                const base = element.replace(/\._(children|count|version)$/, '');
                return CHILDREN_2004[base] || isKnownElement(base) || /^cmi\.(interactions|objectives|comments_from_learner|comments_from_lms)$/.test(base)
                    || /^cmi\.interactions\.\d+\.(objectives|correct_responses)$/.test(base) || /^cmi\.objectives\.\d+\.score$/.test(base)
                    || base === 'adl.data' || element === 'cmi._version' ? 404 : 401;
            }
            if (element.startsWith('adl.nav.request_valid.')) return /^adl\.nav\.request_valid\.(continue|previous|(choice|jump)\.\{target=[^}]+\})$/.test(element) ? 404 : 401;

            if (SCALAR_2004[element]) {
                const def = SCALAR_2004[element];
                if (def.access === 'r') return 404;
                const check = def.check(text);
                if (check) return check;
                value[element] = text;
                return 0;
            }

            if ((match = /^cmi\.interactions\.(\d+)\.(.+)$/.exec(element))) return setInteraction(+match[1], match[2], text);
            if ((match = /^cmi\.objectives\.(\d+)\.(.+)$/.exec(element))) return setObjective(+match[1], match[2], text);
            if ((match = /^cmi\.comments_from_learner\.(\d+)\.(.+)$/.exec(element))) {
                const index = +match[1], field = match[2];
                if (!(field in COMMENT_FIELD)) return 401;
                if (index > commentList.length) return 351;
                const check = COMMENT_FIELD[field](text);
                if (check) return check;
                (commentList[index] || (commentList[index] = {}))[field] = text;
                return 0;
            }
            if ((match = /^cmi\.comments_from_lms\.(\d+)\.(.+)$/.exec(element))) return match[2] in COMMENT_FIELD ? 404 : 401;
            if ((match = /^adl\.data\.(\d+)\.(id|store)$/.exec(element))) {
                const record = sharedList[+match[1]];
                if (match[2] === 'id') return 404;
                if (!record) return 351;
                if (!record.writable) return 404;
                if (text.length > 64000 * 4) return 406;
                record.store = text; record.dirty = true; record.known = true;
                return 0;
            }
            return 401;
        }

        function setInteraction(index, field, text) {
            if (!isInteractionField(field)) return 401;
            if (index > interactionList.length) return 351;
            const isNew = index === interactionList.length;
            if (isNew && field !== 'id') return 408;
            let sub;

            if (field === 'id') {
                const check = INTERACTION_FIELD.id(text);
                if (check) return check;
                if (isNew) interactionList.push({ id: text, objectives: [], correct_responses: [] });
                else interactionList[index].id = text;
                return 0;
            }
            const record = interactionList[index];

            if ((sub = /^objectives\.(\d+)\.id$/.exec(field))) {
                const m = +sub[1];
                if (m > record.objectives.length) return 351;
                const check = V.longId(text);
                if (check) return check;
                if (record.objectives.some((x, i) => x === text && i !== m)) return 351;
                record.objectives[m] = text;
                return 0;
            }
            if ((sub = /^correct_responses\.(\d+)\.pattern$/.exec(field))) {
                if (!record.type) return 408;
                const m = +sub[1];
                if (m > record.correct_responses.length) return 351;
                if (m >= maxPatternCount(record.type)) return 351;
                if (!checkResponse(record.type, text, true)) return 406;
                if (record.type === 'choice' && record.correct_responses.some((x, i) => x === text && i !== m)) return 351;
                record.correct_responses[m] = text;
                return 0;
            }
            if (field === 'learner_response') {
                if (!record.type) return 408;
                if (!checkResponse(record.type, text, false)) return 406;
                record.learner_response = text;
                return 0;
            }
            if (field === 'result') {
                if (!RESULT_VOCAB.includes(text) && !isReal(text)) return 406;
                record.result = text;
                return 0;
            }
            const check = INTERACTION_FIELD[field](text);
            if (check) return check;
            record[field] = text;
            return 0;
        }

        function setObjective(index, field, text) {
            if (!(field in OBJECTIVE_FIELD)) return 401;
            if (index > objectiveList.length) return 351;
            const isNew = index === objectiveList.length;
            if (isNew && field !== 'id') return 408;
            const check = OBJECTIVE_FIELD[field](text);
            if (check) return check;

            if (field === 'id') {
                if (objectiveList.some((x, i) => x.id === text && i !== index)) return 351;
                if (!isNew && objectiveList[index].id !== undefined && objectiveList[index].id !== text) return 351;
                if (isNew) objectiveList.push({ id: text });
                return 0;
            }
            objectiveList[index][field] = text;
            return 0;
        }

        // ----- Ảnh chụp gửi máy chủ -----
        function snapshot() {
            const flat = {};
            for (const [key, v] of Object.entries(value)) {
                if (key === 'adl.nav.request' || key === 'cmi.session_time') continue;
                flat[key] = v;
            }
            interactionList.forEach((record, i) => {
                const prefix = `cmi.interactions.${i}.`;
                for (const [field, v] of Object.entries(record)) {
                    if (field === 'objectives') v.forEach((id, m) => { flat[`${prefix}objectives.${m}.id`] = id; });
                    else if (field === 'correct_responses') v.forEach((p, m) => { flat[`${prefix}correct_responses.${m}.pattern`] = p; });
                    else if (v !== undefined) flat[prefix + field] = v;
                }
            });
            objectiveList.forEach((record, i) => { for (const [field, v] of Object.entries(record)) if (v !== undefined) flat[`cmi.objectives.${i}.${field}`] = v; });
            commentList.forEach((record, i) => { for (const [field, v] of Object.entries(record)) flat[`cmi.comments_from_learner.${i}.${field}`] = v; });
            return flat;
        }

        function sessionSecond() {
            const reported = value['cmi.session_time'] !== undefined ? parseIsoDuration(value['cmi.session_time']) : null;
            return reported !== null ? reported : (initializedAt ? (Date.now() - initializedAt) / 1000 : 0);
        }

        function buildPayload() {
            const sharedWrite = {};
            sharedList.filter(x => x.dirty && x.writable).forEach(x => { sharedWrite[x.id] = x.store; });
            return {
                cmiJson: JSON.stringify(snapshot()),
                interactionCount: interactionList.length,
                report: {
                    completionStatus: evaluatedCompletion(),
                    successStatus: evaluatedSuccess(),
                    scoreScaled: toNumber(value['cmi.score.scaled']),
                    scoreRaw: toNumber(value['cmi.score.raw']),
                    scoreMin: toNumber(value['cmi.score.min']),
                    scoreMax: toNumber(value['cmi.score.max']),
                    progressMeasure: toNumber(value['cmi.progress_measure']),
                    exit: value['cmi.exit'] ?? '',
                    sessionTimeSecond: sessionSecond(),
                    objectiveList: objectiveList.filter(o => o.id).map(o => ({
                        id: o.id,
                        successStatus: o.success_status || 'unknown',
                        completionStatus: o.completion_status || 'unknown',
                        scoreScaled: toNumber(o['score.scaled']),
                        scoreRaw: toNumber(o['score.raw']),
                        scoreMin: toNumber(o['score.min']),
                        scoreMax: toNumber(o['score.max']),
                        progressMeasure: toNumber(o.progress_measure),
                    })),
                    sharedDataWriteById: Object.keys(sharedWrite).length ? sharedWrite : null,
                },
            };
        }

        // ----- Bộ API công khai -----
        const api = {
            Initialize(param) {
                if (param !== '' && param !== undefined) { fail(201, 'Initialize chỉ nhận chuỗi rỗng.'); return 'false'; }
                if (phase === 'running') { fail(103); return 'false'; }
                if (phase === 'terminated') { fail(104); return 'false'; }
                phase = 'running';
                initializedAt = Date.now();
                lastError = 0;
                options.onInitialize?.();
                return 'true';
            },
            Terminate(param) {
                if (param !== '' && param !== undefined) { fail(201, 'Terminate chỉ nhận chuỗi rỗng.'); return 'false'; }
                if (phase === 'idle') { fail(112); return 'false'; }
                if (phase === 'terminated') { options.onLateCall?.('Terminate', ''); fail(113); return 'false'; }
                const navRequest = value['adl.nav.request'] && value['adl.nav.request'] !== '_none_' ? value['adl.nav.request'] : null;
                const payload = buildPayload();
                phase = 'terminated';
                lastError = 0;
                options.onTerminate(payload, navRequest);
                return 'true';
            },
            GetValue(element) { return getValue(element); },
            SetValue(element, v) { return setValue(element, v); },
            Commit(param) {
                if (param !== '' && param !== undefined) { fail(201, 'Commit chỉ nhận chuỗi rỗng.'); return 'false'; }
                if (phase === 'idle') { fail(142); return 'false'; }
                if (phase === 'terminated') { fail(143); return 'false'; }
                lastError = 0;
                options.onCommit(buildPayload());
                return 'true';
            },
            GetLastError() { return String(lastError); },
            GetErrorString(code) { return ERROR_2004[parseInt(code, 10)] || ''; },
            GetDiagnostic(code) {
                if (code === '' || code === undefined || parseInt(code, 10) === lastError) return lastDiagnostic;
                return ERROR_2004[parseInt(code, 10)] || '';
            },
        };

        return {
            api,
            get phase() { return phase; },
            buildPayload,
            /** LMS ép kết thúc khi SCO không tự gọi Terminate (đóng cửa sổ, bấm điều hướng của LMS). */
            forceTerminate() {
                if (phase !== 'running') return null;
                phase = 'terminated';
                return buildPayload();
            },
        };
    }

    // ==================== SCORM 1.2 ====================

    const ERROR_12 = {
        0: 'No error', 101: 'General exception', 201: 'Invalid argument error', 202: 'Element cannot have children',
        203: 'Element not an array - cannot have count', 301: 'Not initialized', 401: 'Not implemented error',
        402: 'Invalid set value, element is a keyword', 403: 'Element is read only', 404: 'Element is write only',
        405: 'Incorrect data type',
    };

    const STATUS_12 = ['passed', 'completed', 'failed', 'incomplete', 'browsed', 'not attempted'];

    const V12 = {
        string: max => v => v.length <= max,
        decimal0to100: v => v === '' || (isReal(v) && parseFloat(v) >= 0 && parseFloat(v) <= 100),
        decimal: v => isReal(v),
        identifier: v => v.length > 0 && v.length <= 255 && /^[\x21-\x7E]+$/.test(v),
        timespan: v => parseCmiTimespan(v) !== null,
        time: v => /^\d{2}:\d{2}:\d{2}(\.\d{1,2})?$/.test(v) && +v.slice(0, 2) < 24 && +v.slice(3, 5) < 60 && +v.slice(6, 8) < 60,
        integerRange: (min, max) => v => /^-?\d+$/.test(v) && +v >= min && +v <= max,
        vocab: list => v => list.includes(v),
    };

    const SCALAR_12 = {
        'cmi.core.student_id': { access: 'r' },
        'cmi.core.student_name': { access: 'r' },
        'cmi.core.lesson_location': { access: 'rw', check: V12.string(255) },
        'cmi.core.credit': { access: 'r' },
        // "not attempted" chỉ LMS được đặt, SCO đặt là sai kiểu (405).
        'cmi.core.lesson_status': { access: 'rw', check: V12.vocab(STATUS_12.filter(x => x !== 'not attempted')) },
        'cmi.core.entry': { access: 'r' },
        'cmi.core.score.raw': { access: 'rw', check: V12.decimal0to100 },
        'cmi.core.score.min': { access: 'rw', check: V12.decimal0to100 },
        'cmi.core.score.max': { access: 'rw', check: V12.decimal0to100 },
        'cmi.core.total_time': { access: 'r' },
        'cmi.core.lesson_mode': { access: 'r' },
        'cmi.core.exit': { access: 'w', check: V12.vocab(['time-out', 'suspend', 'logout', '']) },
        'cmi.core.session_time': { access: 'w', check: V12.timespan },
        'cmi.suspend_data': { access: 'rw', check: V12.string(4096 * 4) },
        'cmi.launch_data': { access: 'r' },
        'cmi.comments': { access: 'rw', check: V12.string(4096 * 4) },
        'cmi.comments_from_lms': { access: 'r' },
        'cmi.student_data.mastery_score': { access: 'r' },
        'cmi.student_data.max_time_allowed': { access: 'r' },
        'cmi.student_data.time_limit_action': { access: 'r' },
        'cmi.student_preference.audio': { access: 'rw', check: V12.integerRange(-1, 100) },
        'cmi.student_preference.language': { access: 'rw', check: V12.string(255) },
        'cmi.student_preference.speed': { access: 'rw', check: V12.integerRange(-100, 100) },
        'cmi.student_preference.text': { access: 'rw', check: V12.integerRange(-1, 1) },
    };

    const CHILDREN_12 = {
        'cmi.core': 'student_id,student_name,lesson_location,credit,lesson_status,entry,score,total_time,lesson_mode,exit,session_time',
        'cmi.core.score': 'raw,min,max',
        'cmi.objectives': 'id,score,status',
        'cmi.student_data': 'mastery_score,max_time_allowed,time_limit_action',
        'cmi.student_preference': 'audio,language,speed,text',
        'cmi.interactions': 'id,objectives,time,type,correct_responses,weighting,student_response,result,latency',
    };

    /**
     * Bộ API SCORM 1.2 (API). Cùng tham số với Scorm2004Api.
     */
    function Scorm12Api(options) {
        const init = options.init;
        let phase = 'idle';
        let lastError = 0;
        let initializedAt = 0;

        const value = {
            'cmi.core.lesson_status': 'not attempted',
            'cmi.student_preference.audio': '0',
            'cmi.student_preference.language': '',
            'cmi.student_preference.speed': '0',
            'cmi.student_preference.text': '0',
        };
        const objectiveList = [];
        const interactionList = [];

        const saved = options.saved || null;
        if (saved) {
            for (const [key, raw] of Object.entries(saved)) {
                const text = String(raw);
                let match;
                if ((match = /^cmi\.objectives\.(\d+)\.(.+)$/.exec(key))) (objectiveList[+match[1]] || (objectiveList[+match[1]] = {}))[match[2]] = text;
                else if ((match = /^cmi\.interactions\.(\d+)\.(.+)$/.exec(key))) {
                    const record = interactionList[+match[1]] || (interactionList[+match[1]] = { objectives: [], correct_responses: [] });
                    let sub;
                    if ((sub = /^objectives\.(\d+)\.id$/.exec(match[2]))) record.objectives[+sub[1]] = text;
                    else if ((sub = /^correct_responses\.(\d+)\.pattern$/.exec(match[2]))) record.correct_responses[+sub[1]] = text;
                    else record[match[2]] = text;
                } else if (SCALAR_12[key] && SCALAR_12[key].access !== 'r' && key !== 'cmi.core.session_time' && key !== 'cmi.core.exit') value[key] = text;
                else if (key === 'cmi.core.lesson_status') value[key] = text;
            }
            [objectiveList, interactionList].forEach(list => { const hole = list.findIndex(x => !x); if (hole >= 0) list.length = hole; });
        }

        const readOnly = {
            'cmi.core.student_id': init.learnerId,
            'cmi.core.student_name': init.learnerName,
            'cmi.core.credit': init.credit,
            'cmi.core.entry': init.entry,
            'cmi.core.total_time': formatCmiTimespan(init.totalTimeSecond || 0),
            'cmi.core.lesson_mode': init.mode === 'normal' ? 'normal' : init.mode,
            'cmi.launch_data': init.launchData || '',
            'cmi.comments_from_lms': '',
            'cmi.student_data.mastery_score': init.masteryScore !== null && init.masteryScore !== undefined ? String(init.masteryScore) : '',
            'cmi.student_data.max_time_allowed': init.maxTimeAllowed || '',
            'cmi.student_data.time_limit_action': init.timeLimitAction || '',
        };

        function fail(code) { lastError = code; return code; }

        function isArrayBase(base) {
            return base === 'cmi.objectives' || base === 'cmi.interactions'
                || /^cmi\.interactions\.\d+\.(objectives|correct_responses)$/.test(base);
        }

        function getValue(element) {
            if (phase !== 'running') { fail(301); return ''; }
            if (typeof element !== 'string' || element === '') { fail(201); return ''; }
            lastError = 0;
            let match;
            if (element.endsWith('._children')) {
                const base = element.slice(0, -10);
                if (CHILDREN_12[base]) return CHILDREN_12[base];
                if (/^cmi\.objectives\.\d+\.score$/.test(base)) return 'raw,min,max';
                if (SCALAR_12[base] || isArrayBase(base)) { fail(202); return ''; }
                fail(201); return '';
            }
            if (element.endsWith('._count')) {
                const base = element.slice(0, -7);
                if (base === 'cmi.objectives') return String(objectiveList.length);
                if (base === 'cmi.interactions') return String(interactionList.length);
                if ((match = /^cmi\.interactions\.(\d+)\.(objectives|correct_responses)$/.exec(base))) {
                    const record = interactionList[+match[1]];
                    return String(record ? record[match[2]].length : 0);
                }
                if (SCALAR_12[base] || CHILDREN_12[base]) { fail(203); return ''; }
                fail(201); return '';
            }
            if (SCALAR_12[element]) {
                const def = SCALAR_12[element];
                if (def.access === 'w') { fail(404); return ''; }
                if (def.access === 'r') return readOnly[element] ?? '';
                return value[element] ?? '';
            }
            if ((match = /^cmi\.objectives\.(\d+)\.(id|score\.raw|score\.min|score\.max|status)$/.exec(element))) {
                const record = objectiveList[+match[1]];
                if (!record) { fail(201); return ''; }
                return record[match[2]] ?? '';
            }
            if (/^cmi\.interactions\.\d+\.(id|time|type|weighting|student_response|result|latency|objectives\.\d+\.id|correct_responses\.\d+\.pattern)$/.test(element)) {
                fail(404); return '';
            }
            fail(401);
            return '';
        }

        function setValue(element, raw) {
            if (phase !== 'running') { fail(301); return 'false'; }
            if (typeof element !== 'string' || element === '') { fail(201); return 'false'; }
            const text = raw === undefined || raw === null ? '' : String(raw);
            lastError = 0;
            const code = doSet(element, text);
            if (code) { fail(code); return 'false'; }
            return 'true';
        }

        function doSet(element, text) {
            let match;
            if (/\._(children|count)$/.test(element)) return 402;
            if (SCALAR_12[element]) {
                const def = SCALAR_12[element];
                if (def.access === 'r') return 403;
                if (!def.check(text)) return 405;
                if (element === 'cmi.comments') value[element] = (value[element] || '') + text;
                else value[element] = text;
                return 0;
            }
            if ((match = /^cmi\.objectives\.(\d+)\.(id|score\.raw|score\.min|score\.max|status)$/.exec(element))) {
                const index = +match[1], field = match[2];
                if (index > objectiveList.length) return 201;
                const check = field === 'id' ? V12.identifier(text)
                    : field === 'status' ? STATUS_12.includes(text)
                    : V12.decimal0to100(text);
                if (!check) return 405;
                (objectiveList[index] || (objectiveList[index] = {}))[field] = text;
                return 0;
            }
            if ((match = /^cmi\.interactions\.(\d+)\.(.+)$/.exec(element))) {
                const index = +match[1], field = match[2];
                if (index > interactionList.length) return 201;
                const record = interactionList[index] || { objectives: [], correct_responses: [] };
                let sub, ok;
                if ((sub = /^objectives\.(\d+)\.id$/.exec(field))) {
                    if (+sub[1] > record.objectives.length) return 201;
                    if (!V12.identifier(text)) return 405;
                    record.objectives[+sub[1]] = text;
                } else if ((sub = /^correct_responses\.(\d+)\.pattern$/.exec(field))) {
                    if (+sub[1] > record.correct_responses.length) return 201;
                    if (text.length > 255 * 4) return 405;
                    record.correct_responses[+sub[1]] = text;
                } else {
                    switch (field) {
                        case 'id': ok = V12.identifier(text); break;
                        case 'time': ok = V12.time(text); break;
                        case 'type': ok = ['true-false', 'choice', 'fill-in', 'matching', 'performance', 'sequencing', 'likert', 'numeric'].includes(text); break;
                        case 'weighting': ok = V12.decimal(text); break;
                        case 'student_response': ok = text.length <= 255 * 4; break;
                        case 'result': ok = ['correct', 'wrong', 'unanticipated', 'neutral'].includes(text) || V12.decimal(text); break;
                        case 'latency': ok = V12.timespan(text); break;
                        default: return 401;
                    }
                    if (!ok) return 405;
                    record[field] = text;
                }
                interactionList[index] = record;
                return 0;
            }
            if (/^cmi\.(core|student_data|student_preference|objectives|interactions)(\.|$)/.test(element)) return 401;
            return 401;
        }

        /** Luật kết thúc của 1.2: SCO không tự đặt trạng thái thì LMS ghi "completed"; có điểm mà gói khai mastery
         *  score thì LMS chấm passed / failed theo điểm (RTE 1.2 mục 3.4.4). */
        function applyFinishRules() {
            const credit = readOnly['cmi.core.credit'] === 'credit';
            if (value['cmi.core.lesson_status'] === 'not attempted') value['cmi.core.lesson_status'] = 'completed';
            const mastery = toNumber(readOnly['cmi.student_data.mastery_score']);
            const raw = toNumber(value['cmi.core.score.raw']);
            if (credit && mastery !== null && raw !== null) value['cmi.core.lesson_status'] = raw >= mastery ? 'passed' : 'failed';
        }

        function snapshot() {
            const flat = {};
            for (const [key, v] of Object.entries(value)) if (key !== 'cmi.core.session_time') flat[key] = v;
            objectiveList.forEach((record, i) => { for (const [field, v] of Object.entries(record)) flat[`cmi.objectives.${i}.${field}`] = v; });
            interactionList.forEach((record, i) => {
                const prefix = `cmi.interactions.${i}.`;
                for (const [field, v] of Object.entries(record)) {
                    if (field === 'objectives') v.forEach((id, m) => { flat[`${prefix}objectives.${m}.id`] = id; });
                    else if (field === 'correct_responses') v.forEach((p, m) => { flat[`${prefix}correct_responses.${m}.pattern`] = p; });
                    else flat[prefix + field] = v;
                }
            });
            return flat;
        }

        function scaledFrom(raw, min, max) {
            if (raw === null) return null;
            const low = min === null ? 0 : min, high = max === null ? 100 : max;
            if (high <= low) return Math.max(-1, Math.min(1, raw / 100));
            return Math.max(-1, Math.min(1, (raw - low) / (high - low)));
        }

        function statusToCompletion(status) {
            if (['passed', 'completed', 'failed'].includes(status)) return 'completed';
            if (['incomplete', 'browsed'].includes(status)) return 'incomplete';
            return status === 'not attempted' ? 'not attempted' : 'unknown';
        }
        function statusToSuccess(status) { return status === 'passed' || status === 'failed' ? status : 'unknown'; }

        function buildPayload() {
            const status = value['cmi.core.lesson_status'];
            const raw = toNumber(value['cmi.core.score.raw']), min = toNumber(value['cmi.core.score.min']), max = toNumber(value['cmi.core.score.max']);
            const reported = value['cmi.core.session_time'] !== undefined ? parseCmiTimespan(value['cmi.core.session_time']) : null;
            return {
                cmiJson: JSON.stringify(snapshot()),
                interactionCount: interactionList.length,
                report: {
                    completionStatus: statusToCompletion(status),
                    successStatus: statusToSuccess(status),
                    scoreScaled: scaledFrom(raw, min, max),
                    scoreRaw: raw, scoreMin: min, scoreMax: max,
                    progressMeasure: null,
                    exit: value['cmi.core.exit'] ?? '',
                    sessionTimeSecond: reported !== null ? reported : (initializedAt ? (Date.now() - initializedAt) / 1000 : 0),
                    objectiveList: objectiveList.filter(o => o.id).map(o => ({
                        id: o.id,
                        successStatus: statusToSuccess(o.status),
                        completionStatus: statusToCompletion(o.status || 'unknown'),
                        scoreScaled: scaledFrom(toNumber(o['score.raw']), toNumber(o['score.min']), toNumber(o['score.max'])),
                        scoreRaw: toNumber(o['score.raw']), scoreMin: toNumber(o['score.min']), scoreMax: toNumber(o['score.max']),
                        progressMeasure: null,
                    })),
                    sharedDataWriteById: null,
                },
            };
        }

        const api = {
            LMSInitialize(param) {
                if (param !== '' && param !== undefined) { fail(201); return 'false'; }
                if (phase !== 'idle') { fail(101); return 'false'; }
                phase = 'running';
                initializedAt = Date.now();
                lastError = 0;
                options.onInitialize?.();
                return 'true';
            },
            LMSFinish(param) {
                if (param !== '' && param !== undefined) { fail(201); return 'false'; }
                if (phase !== 'running') { fail(301); return 'false'; }
                applyFinishRules();
                const payload = buildPayload();
                phase = 'terminated';
                lastError = 0;
                options.onTerminate(payload, null);
                return 'true';
            },
            LMSGetValue(element) { return getValue(element); },
            LMSSetValue(element, v) { return setValue(element, v); },
            LMSCommit(param) {
                if (param !== '' && param !== undefined) { fail(201); return 'false'; }
                if (phase !== 'running') { fail(301); return 'false'; }
                lastError = 0;
                options.onCommit(buildPayload());
                return 'true';
            },
            LMSGetLastError() { return String(lastError); },
            LMSGetErrorString(code) { return ERROR_12[parseInt(code, 10)] || ''; },
            LMSGetDiagnostic(code) { return ERROR_12[parseInt(code === '' || code === undefined ? lastError : code, 10)] || ''; },
        };

        return {
            api,
            get phase() { return phase; },
            buildPayload,
            forceTerminate() {
                if (phase !== 'running') return null;
                applyFinishRules();
                phase = 'terminated';
                return buildPayload();
            },
        };
    }

    global.ScormRte = {
        Scorm2004Api, Scorm12Api,
        parseIsoDuration, formatIsoDuration, parseCmiTimespan, formatCmiTimespan,
        checkResponse,
    };
})(window);
