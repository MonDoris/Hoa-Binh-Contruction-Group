// File: Application/Features/Scorm/Export/ScormExportTemplates.cs

namespace HbELearning.Application.Features.Scorm.Export;

/// <summary>
/// Mã nguồn tĩnh đóng kèm mọi gói xuất ra: thư viện nói chuyện với LMS (hbe-scorm.js), giao diện (hbe-style.css),
/// và khung trang của một SCO bài học / bài kiểm tra.
///
/// Gói xuất ra phải chạy được trên LMS BẤT KỲ (Moodle, SCORM Cloud, Cornerstone...), nên ở đây không được dùng
/// thứ gì của HB E-Learning: không gọi API của hệ thống, không nạp font hay thư viện từ mạng — mở gói khi không có
/// Internet vẫn phải học được.
/// </summary>
public static class ScormExportTemplates
{
    /// <summary>Thư viện SCORM 2004 dùng chung cho các SCO xuất ra. Tìm API_1484_11 theo đúng thuật toán của ADL
    /// (đi ngược lên cửa sổ cha, rồi tới cửa sổ mở ra nó), ghi session_time lúc kết thúc, và vẫn cho học (không
    /// theo dõi) khi trang được mở trần ngoài LMS.</summary>
    public const string RuntimeScript = """
/* HB E-Learning — thư viện SCORM 2004 4th Edition cho SCO xuất ra */
var HBE = (function () {
  var api = null, started = false, finished = false, startAt = new Date();

  function scan(win) {
    var tries = 0;
    while (win && !win.API_1484_11 && win.parent && win.parent !== win && tries < 500) { tries++; win = win.parent; }
    return win ? win.API_1484_11 || null : null;
  }
  function find() {
    try {
      var found = null;
      if (window.parent && window.parent !== window) found = scan(window.parent);
      if (!found && window.opener) found = scan(window.opener);
      if (!found && window.top && window.top.opener) found = scan(window.top.opener);
      return found;
    } catch (e) { return null; }
  }
  function pad(n) { return (n < 10 ? '0' : '') + n; }

  /** Thời lượng ISO 8601 (timeinterval của SCORM 2004), làm tròn tới 1/100 giây. */
  function duration(ms) {
    var cs = Math.max(0, Math.round(ms / 10)), h = Math.floor(cs / 360000), m = Math.floor(cs % 360000 / 6000),
        s = (cs % 6000) / 100;
    return 'PT' + h + 'H' + m + 'M' + s.toFixed(2).replace(/\.00$/, '') + 'S';
  }
  function timestamp(d) {
    d = d || new Date();
    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
  }

  function start() {
    if (started) return !!api;
    started = true;
    api = find();
    if (!api) return false;
    if (String(api.Initialize('')) !== 'true') { api = null; return false; }
    window.addEventListener('pagehide', function () { finish(); });
    window.addEventListener('beforeunload', function () { finish(); });
    return true;
  }
  function get(name) { return api ? String(api.GetValue(name)) : ''; }
  function set(name, value) { return api ? String(api.SetValue(name, String(value))) === 'true' : false; }
  function commit() { return api ? String(api.Commit('')) === 'true' : false; }
  function finish(navRequest) {
    if (!api || finished) return;
    finished = true;
    set('cmi.session_time', duration(new Date() - startAt));
    if (navRequest) set('adl.nav.request', navRequest);
    commit();
    api.Terminate('');
  }
  function canNavigate(request) { return api ? get('adl.nav.request_valid.' + request) === 'true' : false; }

  return { start: start, get: get, set: set, commit: commit, finish: finish, canNavigate: canNavigate,
           duration: duration, timestamp: timestamp, get connected() { return !!api; } };
})();
""";

    public const string StyleSheet = """
:root { --brand: #00a1e6; --ink: #0f172a; --muted: #64748b; --line: #e2e8f0; --bg: #f8fafc; --ok: #059669; --bad: #dc2626; }
* { box-sizing: border-box; }
body { margin: 0; font: 16px/1.6 "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif; color: var(--ink); background: var(--bg); }
.wrap { max-width: 920px; margin: 0 auto; padding: 28px 20px 96px; }
h1 { font-size: 26px; line-height: 1.25; margin: 0 0 6px; }
.meta { color: var(--muted); font-size: 14px; margin-bottom: 22px; }
.card { background: #fff; border: 1px solid var(--line); border-radius: 14px; padding: 22px; margin-bottom: 18px; }
.lesson-body img, .lesson-body video, .lesson-body iframe { max-width: 100%; }
.lesson-body table { border-collapse: collapse; max-width: 100%; }
.lesson-body td, .lesson-body th { border: 1px solid var(--line); padding: 6px 8px; }
video.media { width: 100%; border-radius: 12px; background: #000; }
iframe.media { width: 100%; height: 72vh; border: 1px solid var(--line); border-radius: 12px; background: #fff; }
img.media { max-width: 100%; border-radius: 12px; }
.bar { position: fixed; left: 0; right: 0; bottom: 0; background: #fff; border-top: 1px solid var(--line); padding: 12px 20px; display: flex; gap: 12px; align-items: center; justify-content: flex-end; flex-wrap: wrap; }
.bar .note { margin-right: auto; color: var(--muted); font-size: 14px; }
button, .btn { font: inherit; font-weight: 600; border: 0; border-radius: 10px; padding: 10px 18px; cursor: pointer; background: var(--brand); color: #fff; text-decoration: none; display: inline-block; }
button.ghost, .btn.ghost { background: #fff; color: var(--ink); border: 1px solid var(--line); }
button:disabled { opacity: .55; cursor: not-allowed; }
.question { border: 1px solid var(--line); border-radius: 12px; padding: 16px; margin-bottom: 14px; background: #fff; }
.question h3 { font-size: 16px; margin: 0 0 10px; }
.answer { display: flex; gap: 10px; align-items: flex-start; padding: 8px 10px; border-radius: 8px; cursor: pointer; }
.answer:hover { background: var(--bg); }
.answer.correct { background: #ecfdf5; }
.answer.wrong { background: #fef2f2; }
.explain { margin-top: 8px; font-size: 14px; color: var(--muted); }
.result { text-align: center; }
.result .score { font-size: 44px; font-weight: 800; }
.result.ok .score { color: var(--ok); }
.result.bad .score { color: var(--bad); }
.offline { background: #fff7ed; border: 1px solid #fed7aa; color: #9a3412; border-radius: 10px; padding: 10px 14px; font-size: 14px; margin-bottom: 16px; }
.timer { font-variant-numeric: tabular-nums; font-weight: 700; }
""";

    /// <summary>Khung SCO bài học. Thay {{TITLE}}, {{META}}, {{MEDIA}}, {{BODY}}.</summary>
    public const string LessonPage = """
<!DOCTYPE html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{TITLE}}</title>
<link rel="stylesheet" href="../shared/hbe-style.css">
<script src="../shared/hbe-scorm.js"></script>
</head>
<body>
<div class="wrap">
  <div id="offline" class="offline" hidden>Đang mở ngoài hệ thống quản lý học tập (LMS) — vẫn xem được nội dung nhưng tiến độ sẽ không được ghi lại.</div>
  <h1>{{TITLE}}</h1>
  <div class="meta">{{META}}</div>
  {{MEDIA}}
  <div class="card lesson-body">{{BODY}}</div>
</div>
<div class="bar">
  <span class="note" id="note">Đọc / xem hết nội dung rồi bấm hoàn thành.</span>
  <button type="button" id="btn-done">Đánh dấu hoàn thành</button>
  <button type="button" id="btn-next" class="ghost" hidden>Bài tiếp theo →</button>
</div>
<script>
(function () {
  var connected = HBE.start();
  document.getElementById('offline').hidden = connected;
  var done = connected && HBE.get('cmi.completion_status') === 'completed';
  var note = document.getElementById('note'), btnDone = document.getElementById('btn-done'), btnNext = document.getElementById('btn-next');

  function render() {
    btnDone.disabled = done;
    btnDone.textContent = done ? 'Đã hoàn thành ✓' : 'Đánh dấu hoàn thành';
    note.textContent = done ? 'Bạn đã hoàn thành bài học này.' : 'Đọc / xem hết nội dung rồi bấm hoàn thành.';
    btnNext.hidden = !(done && HBE.canNavigate('continue'));
  }

  if (connected) {
    if (HBE.get('cmi.completion_status') === 'unknown' || HBE.get('cmi.completion_status') === 'not attempted')
      HBE.set('cmi.completion_status', 'incomplete');
    HBE.set('cmi.exit', done ? 'normal' : 'suspend');
    HBE.commit();
  }

  btnDone.addEventListener('click', function () {
    done = true;
    if (connected) {
      HBE.set('cmi.completion_status', 'completed');
      HBE.set('cmi.success_status', 'passed');
      HBE.set('cmi.progress_measure', '1');
      HBE.set('cmi.exit', 'normal');
      HBE.commit();
    }
    render();
  });
  btnNext.addEventListener('click', function () { HBE.finish('continue'); });
  render();
})();
</script>
</body>
</html>
""";

    /// <summary>Khung SCO bài kiểm tra. Thay {{TITLE}}, {{META}}, {{QUIZ_JSON}}.</summary>
    public const string QuizPage = """
<!DOCTYPE html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{TITLE}}</title>
<link rel="stylesheet" href="../shared/hbe-style.css">
<script src="../shared/hbe-scorm.js"></script>
</head>
<body>
<div class="wrap">
  <div id="offline" class="offline" hidden>Đang mở ngoài hệ thống quản lý học tập (LMS) — vẫn làm bài được nhưng điểm sẽ không được ghi lại.</div>
  <h1>{{TITLE}}</h1>
  <div class="meta">{{META}}</div>
  <div id="result"></div>
  <form id="quiz"></form>
</div>
<div class="bar">
  <span class="note" id="note"></span>
  <button type="button" id="btn-submit">Nộp bài</button>
  <button type="button" id="btn-retry" class="ghost" hidden>Làm lại</button>
  <button type="button" id="btn-next" class="ghost" hidden>Bài tiếp theo →</button>
</div>
<script>
(function () {
  var QUIZ = {{QUIZ_JSON}};
  var connected = HBE.start();
  document.getElementById('offline').hidden = connected;

  var form = document.getElementById('quiz'), resultBox = document.getElementById('result'), note = document.getElementById('note');
  var btnSubmit = document.getElementById('btn-submit'), btnRetry = document.getElementById('btn-retry'), btnNext = document.getElementById('btn-next');
  var startAt = new Date(), timer = null, submitted = false;

  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function shuffle(list) { for (var i = list.length - 1; i > 0; i--) { var j = Math.floor(Math.random() * (i + 1)); var t = list[i]; list[i] = list[j]; list[j] = t; } return list; }

  function renderQuestions() {
    var list = QUIZ.shuffle ? shuffle(QUIZ.questions.slice()) : QUIZ.questions;
    form.innerHTML = list.map(function (q, i) {
      var type = q.multiple ? 'checkbox' : 'radio';
      return '<div class="question" data-q="' + q.id + '"><h3>Câu ' + (i + 1) + '. ' + esc(q.content) + '</h3>'
        + q.answers.map(function (a) {
            return '<label class="answer" data-a="' + a.id + '"><input type="' + type + '" name="q' + q.id + '" value="' + a.id + '"> <span>' + esc(a.content) + '</span></label>';
          }).join('')
        + '<div class="explain" hidden></div></div>';
    }).join('');
  }

  function startTimer() {
    if (!QUIZ.timeLimitMinutes) { note.textContent = QUIZ.questions.length + ' câu hỏi · Điểm đạt ' + QUIZ.passScore + '%'; return; }
    var end = Date.now() + QUIZ.timeLimitMinutes * 60000;
    function tick() {
      var left = Math.max(0, end - Date.now()), m = Math.floor(left / 60000), s = Math.floor(left % 60000 / 1000);
      note.innerHTML = 'Thời gian còn lại: <span class="timer">' + m + ':' + (s < 10 ? '0' : '') + s + '</span>';
      if (left <= 0) submit();
    }
    tick(); timer = setInterval(tick, 1000);
  }

  function submit() {
    if (submitted) return;
    submitted = true;
    if (timer) clearInterval(timer);
    var earned = 0, total = 0, now = new Date();
    var base = connected ? parseInt(HBE.get('cmi.interactions._count') || '0', 10) || 0 : 0;

    QUIZ.questions.forEach(function (q, i) {
      total += q.score;
      var picked = Array.prototype.slice.call(form.querySelectorAll('input[name="q' + q.id + '"]:checked')).map(function (x) { return x.value; });
      var correct = q.answers.filter(function (a) { return a.correct; }).map(function (a) { return String(a.id); });
      var ok = picked.length === correct.length && picked.every(function (x) { return correct.indexOf(x) >= 0; });
      if (ok) earned += q.score;

      var box = form.querySelector('[data-q="' + q.id + '"]');
      q.answers.forEach(function (a) {
        var row = box.querySelector('[data-a="' + a.id + '"]');
        if (a.correct) row.classList.add('correct'); else if (picked.indexOf(String(a.id)) >= 0) row.classList.add('wrong');
      });
      if (q.explanation) { var ex = box.querySelector('.explain'); ex.hidden = false; ex.textContent = 'Giải thích: ' + q.explanation; }

      if (connected) {
        var n = 'cmi.interactions.' + (base + i) + '.';
        HBE.set(n + 'id', 'q' + q.id);
        HBE.set(n + 'type', 'choice');
        HBE.set(n + 'timestamp', HBE.timestamp(now));
        HBE.set(n + 'weighting', q.score);
        HBE.set(n + 'correct_responses.0.pattern', correct.map(function (x) { return 'a' + x; }).join('[,]'));
        HBE.set(n + 'learner_response', picked.map(function (x) { return 'a' + x; }).join('[,]'));
        HBE.set(n + 'result', ok ? 'correct' : 'incorrect');
        HBE.set(n + 'latency', HBE.duration(now - startAt));
        HBE.set(n + 'description', String(q.content).slice(0, 250));
      }
    });

    var percent = total > 0 ? Math.round(earned / total * 10000) / 100 : 0;
    var passed = percent >= QUIZ.passScore;
    form.querySelectorAll('input').forEach(function (x) { x.disabled = true; });
    resultBox.innerHTML = '<div class="card result ' + (passed ? 'ok' : 'bad') + '"><div class="score">' + percent + '%</div>'
      + '<div>' + (passed ? 'Đạt — chúc mừng bạn!' : 'Chưa đạt — cần ' + QUIZ.passScore + '% để đạt.') + '</div>'
      + '<div class="meta">' + earned + ' / ' + total + ' điểm</div></div>';
    window.scrollTo(0, 0);

    if (connected) {
      HBE.set('cmi.score.raw', earned);
      HBE.set('cmi.score.min', 0);
      HBE.set('cmi.score.max', total);
      HBE.set('cmi.score.scaled', total > 0 ? (earned / total).toFixed(4) : 0);
      HBE.set('cmi.completion_status', 'completed');
      HBE.set('cmi.success_status', passed ? 'passed' : 'failed');
      HBE.set('cmi.exit', 'normal');
      HBE.commit();
    }
    btnSubmit.hidden = true;
    btnRetry.hidden = passed;
    btnNext.hidden = !(passed && HBE.canNavigate('continue'));
    note.textContent = passed ? 'Bạn đã đạt bài kiểm tra.' : 'Bạn có thể làm lại.';
  }

  btnSubmit.addEventListener('click', function () { submit(); });
  btnRetry.addEventListener('click', function () {
    submitted = false; startAt = new Date(); resultBox.innerHTML = '';
    btnSubmit.hidden = false; btnRetry.hidden = true; btnNext.hidden = true;
    renderQuestions(); startTimer();
  });
  btnNext.addEventListener('click', function () { HBE.finish('continue'); });

  if (connected) {
    if (HBE.get('cmi.completion_status') !== 'completed') HBE.set('cmi.completion_status', 'incomplete');
    HBE.commit();
  }
  renderQuestions();
  startTimer();
})();
</script>
</body>
</html>
""";
}
