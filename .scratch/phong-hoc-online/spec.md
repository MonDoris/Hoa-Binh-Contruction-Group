# Làm mới chức năng Phòng học online: giao diện, ghi hình có thể tạm dừng, và Lịch sử

**Status:** ready-for-agent

Thuật ngữ dùng đúng theo `CONTEXT.md` ở gốc repo: Buổi học, Chủ trì, Người tham gia, Màn hình xem trước, Ghi hình, Tạm dừng, Dừng ghi, Bản ghi, Đồng hồ ghi hình, Diễn biến, Lịch sử.

## Problem Statement

Người dùng Phòng học online đang gặp bốn vấn đề.

**Giao diện.** Trang danh sách chỉ là một bảng khô khan: không thấy ngay buổi nào đang diễn ra, các nút Sửa, Hủy, Xóa bị tô màu vàng, cam, đỏ cứng. Bên trong phòng, giao diện tối tự dựng không theo bộ màu của hệ thống, khung chat có bề rộng cố định và thanh điều khiển đặt tuyệt đối nên dùng trên máy tính bảng hoặc điện thoại rất khó. Chủ trì vừa vào phòng là buổi học bắt đầu ngay, không có dịp kiểm tra camera và mic; học viên vào sớm khi chủ trì chưa bắt đầu chỉ nhận một thông báo lỗi sau khi đã bấm vào.

**Ghi hình một chiều.** Bấm Ghi hình là nút bị khóa; chủ trì không tạm dừng được để nghỉ giải lao, cũng không dừng được trừ khi rời phòng. Bất kỳ ai trong phòng, kể cả học viên, đều bấm ghi được, và server không kiểm tra điều đó. Mọi người khác chỉ thấy chữ "Đang ghi hình" với một đồng hồ chạy từ lúc họ nhận tin, nên người vào muộn thấy 00:00 dù buổi đã ghi cả tiếng.

**Lịch sử nghèo nàn.** Buổi đã kết thúc chỉ có một nút "Xem lại" dẫn tới video thô và danh sách diễn biến. Không có thời lượng, không có thống kê, không tìm được buổi cũ.

**Riêng tư.** Mọi tài khoản đăng nhập, kể cả Giảng Viên bên ngoài, xem được bản ghi và toàn bộ nội dung chat của mọi buổi học, kể cả buổi mình chưa từng tham gia.

## Solution

Làm lại giao diện bên ngoài lẫn bên trong, thêm Màn hình xem trước trước khi vào phòng, cho chủ trì Tạm dừng, Tiếp tục và Dừng ghi, và dựng một khu Lịch sử đầy đủ với quyền xem chặt chẽ.

- **Màn hình xem trước.** Mọi người, kể cả chủ trì, kiểm tra camera và mic trước khi vào. Buổi học chỉ chính thức bắt đầu khi chủ trì rời màn hình này để vào phòng. Học viên đến sớm thấy trạng thái chờ và tự được mở nút Tham gia khi buổi bắt đầu.
- **Ghi hình.** Chỉ chủ trì điều khiển. Bấm Ghi thì hiện Tạm dừng/Tiếp tục và Dừng ghi. Tạm dừng giữ một file và cắt đoạn tạm dừng khỏi video. Dừng ghi chốt Bản ghi, ghi lại sau đó tạo Bản ghi mới. Mọi người thấy "Đang ghi mm:ss" hoặc "Tạm dừng ghi"; đồng hồ không tính phần tạm dừng và người vào muộn thấy đúng số.
- **Trang ngoài.** Dải Đang diễn ra nổi bật, thẻ Sắp diễn ra có đếm ngược, và tab Lịch sử.
- **Lịch sử.** Danh sách thẻ có thời lượng, số người, số Bản ghi, dung lượng, tìm kiếm và bộ lọc. Trang xem lại có trình phát, thống kê, Diễn biến lọc theo loại và bấm một dòng thì video nhảy tới đúng thời điểm.
- **Quyền xem.** Chỉ chủ trì, Admin và người từng vào buổi đó xem được Lịch sử của buổi; người khác không thấy và nhận 403.

## User Stories

### Màn hình xem trước

1. As a Người tham gia, I want to see my camera preview before entering a Buổi học, so that I know how I will look before others see me.
2. As a Người tham gia, I want a live microphone level meter on the Màn hình xem trước, so that I can tell my mic works without having to speak into an empty room.
3. As a Người tham gia, I want to choose which camera and which microphone to use when my computer has several, so that I do not enter with the wrong device.
4. As a Người tham gia, I want to switch my camera and microphone on or off before entering, so that I decide how I appear from the first second.
5. As a Chủ trì, I want camera and microphone to default to on in the Màn hình xem trước, so that I am ready to teach as soon as I enter.
6. As a học viên, I want camera and microphone to default to off in the Màn hình xem trước, so that I do not interrupt the Buổi học by accident.
7. As a Người tham gia, I want the devices I picked in the Màn hình xem trước to be the ones used inside the room, so that I do not have to choose twice.
8. As a Người tham gia whose browser denied camera or microphone permission, I want to still enter the room as a listener with a clear explanation, so that a permission problem does not lock me out of the class.
9. As a Chủ trì, I want to check my devices before the Buổi học officially starts, so that students are not watching me fix my setup.
10. As a Chủ trì, I want the Buổi học to start only when I leave the Màn hình xem trước and enter the room, so that I control the moment it begins.
11. As a học viên who arrives before the Chủ trì has started, I want the Màn hình xem trước to tell me the host has not started yet, so that I understand why I cannot enter.
12. As a học viên waiting early, I want the page to recheck every few seconds and enable the Tham gia button once the Buổi học is live, so that I do not have to keep refreshing.
13. As a học viên who opened an invitation link with a join code, I want the Màn hình xem trước to verify the code and the Buổi học before showing the Tham gia button, so that I learn early if the link is wrong or the Buổi học is over.

### Trong buổi học

14. As a Người tham gia, I want the room to use the same colours and typography as the rest of the system, so that it feels like one product.
15. As a Người tham gia, I want a clear main stage showing the Chủ trì, so that I always know where to look.
16. As a Người tham gia, I want chat and the participant list in a side panel I can open and close, so that I can give the video more room when I want.
17. As a Người tham gia on a phone, I want the side panel to slide up from the bottom, so that the room is usable on a small screen.
18. As a Người tham gia on a tablet or phone, I want the control bar to fit and stay reachable with my thumb, so that I can mute, leave or chat without hunting for buttons.
19. As a Người tham gia, I want a top bar with the Buổi học title, its status, how long it has been running, the invitation code and the recording state, so that I see the essentials at a glance.
20. As a Chủ trì, I want to copy the invitation link from the top bar, so that I can invite late students quickly.
21. As a Người tham gia, I want each control to show its state with both an icon and a label or tooltip, so that I never have to guess whether my mic is on.
22. As a Người tham gia using a screen reader or keyboard, I want every control reachable by keyboard with visible focus and a text name, so that I can use the room without a mouse.
23. As a Chủ trì, I want to see a tile for each student with their camera or initials and mic state, so that I can tell who is present and who is speaking.

### Ghi hình

24. As a Chủ trì, I want to start recording with one button, so that I can capture the Buổi học.
25. As a Chủ trì, I want that button to turn into a Tạm dừng button and a separate Dừng ghi button once recording starts, so that both actions are always obvious.
26. As a Chủ trì, I want to Tạm dừng recording during a break, so that the break is not in the Bản ghi.
27. As a Chủ trì, I want to Tiếp tục after pausing and keep adding to the same Bản ghi, so that one session does not split into many files because of one break.
28. As a Chủ trì, I want Dừng ghi to finish the Bản ghi, so that it becomes a finished, playable file.
29. As a Chủ trì, I want to start recording again after Dừng ghi and get a new Bản ghi, so that I can record a second part of the class separately.
30. As a Chủ trì, I want recording to be finalized automatically if I end the Buổi học or leave the room while recording or paused, so that I never lose a Bản ghi to a forgotten button.
31. As a Chủ trì, I want to see my own Đồng hồ ghi hình while recording, so that I know how much has been captured.
32. As a Chủ trì whose browser stops screen sharing from its own banner, I want recording to finalize cleanly, so that the Bản ghi is still saved and the state is correct for everyone.
33. As a học viên, I do not want a recording control at all, so that I cannot capture or interfere with a recording by accident.
34. As a học viên, I want the system to refuse a recording request that does not come from the Chủ trì, even if someone calls the service directly, so that recording rights cannot be bypassed.
35. As a Người tham gia, I want to see "Đang ghi mm:ss" while the Chủ trì is recording, so that I know I am being recorded.
36. As a Người tham gia, I want to see "Tạm dừng ghi" while recording is paused, so that I know I am not being recorded at that moment.
37. As a Người tham gia who joins late, I want to see the correct recording state and time immediately on entering, so that it does not say 00:00 when 45 minutes have been recorded.
38. As a Người tham gia, I want the Đồng hồ ghi hình to exclude paused time, so that it matches the length of the Bản ghi.
39. As a Chủ trì, I want every start, pause, resume and stop of recording written into the Diễn biến, so that the history shows exactly when recording happened.

### Trang ngoài

40. As a Người tham gia, I want a prominent Đang diễn ra section at the top, so that I can join a live Buổi học immediately.
41. As a Người tham gia, I want upcoming Buổi học shown as cards with a countdown to the start, so that I can plan my time.
42. As a Chủ trì, I want Bắt đầu, Sửa, Hủy and copy-invitation actions on my upcoming cards, so that I manage them in one place.
43. As a Chủ trì, I want to create a Buổi học and start it right away from the same dialog, so that an impromptu class takes seconds.
44. As a Người tham gia with a code, I want a polished six-character code entry dialog, so that joining by code is quick and clear.
45. As a user, I want the action buttons to use the system's standard colours instead of hard-coded yellow, orange and red, so that the page looks consistent in light and dark themes.
46. As a user, I want everything on the outer page to follow the system's light or dark theme and work on a phone, so that I can use it anywhere.

### Lịch sử và xem lại

47. As a Người tham gia, I want a Lịch sử tab listing the Buổi học I took part in, so that I can find a past class.
48. As a Chủ trì, I want my own Buổi học to appear in Lịch sử, so that I can review and share what I taught.
49. As an Admin, I want every ended Buổi học to appear in Lịch sử, so that I can supervise training.
50. As a Người tham gia, I want each Lịch sử card to show title, date, Chủ trì, real duration, number of participants, number of Bản ghi and total size, so that I can recognise a Buổi học without opening it.
51. As a Người tham gia, I want to search Lịch sử by title or host name, so that I can find a class quickly.
52. As a Người tham gia, I want to filter Lịch sử by date range, so that I can look at a particular week or month.
53. As a Người tham gia, I want to filter Lịch sử to only the Buổi học I hosted or joined, so that an Admin's long list does not bury mine.
54. As a Người tham gia opening a past Buổi học, I want a large video player and a way to choose between several Bản ghi of that Buổi học, so that I can watch each part.
55. As a Người tham gia, I want to see the duration of each Bản ghi, so that I can choose which to watch.
56. As a Người tham gia, I want a short summary of the Buổi học (duration, participants, number of chat messages), so that I grasp it in seconds.
57. As a Người tham gia, I want the Diễn biến filterable by type (chat, joins and leaves, microphone and camera, recording), so that I can find the chat or the break quickly.
58. As a Người tham gia, I want to click a line in the Diễn biến and have the video jump to that moment, so that I can find what was said without scrubbing.
59. As a Người tham gia, I want the jump to account for paused periods, so that it lands on the right second even if recording was paused earlier.
60. As a Chủ trì or Admin, I want a download button on each Bản ghi, so that I can keep or share the file.
61. As a Người tham gia who joined late or left early, I want to see the whole Bản ghi and Diễn biến, including the parts when I was not present, so that I can catch up on what I missed.
62. As a Buổi học with no Bản ghi, I want the review page to still show the summary and Diễn biến with a clear note that no video exists, so that nothing looks broken.
63. As an older Buổi học recorded before this change, I want it to still appear and play, even without a stored duration, so that existing history is not lost.

### Quyền và riêng tư

64. As a Người tham gia who never entered a particular Buổi học, I do not want it to appear in my Lịch sử, so that private meetings stay private.
65. As a Người tham gia who never entered a particular Buổi học, I want the system to refuse access to its Bản ghi and Diễn biến even if I know its address, so that the rule cannot be bypassed.
66. As a Giảng Viên bên ngoài, I want to see only the Buổi học I took part in, so that internal company meetings are not exposed to me.
67. As a Người tham gia, I want my right to view a Buổi học to depend only on having entered it, so that the rule is simple and predictable.
68. As an Admin, I want to remain the only person who can delete a Buổi học and its Bản ghi, so that retention stays under central control.

## Implementation Decisions

**Recording clock.** A new pure module in the application layer models the recording state of one live Buổi học: Idle, Recording, Paused. Transitions are Start, Pause, Resume and Stop, each taking the current time as an argument. It reports the elapsed recorded time excluding paused spans and rejects or ignores invalid transitions without changing state. Keeping it free of I/O makes it directly testable. The server holds one instance per live Buổi học for as long as the participant list for that room exists, and discards it when the Buổi học ends.

**Session service.** The live-session service gains the rules for recording control: only the Chủ trì may start, pause, resume or stop; each accepted transition updates the clock and writes a Diễn biến entry. Late joiners are served the current state and elapsed time from the same clock. Ending a Buổi học, or the Chủ trì disconnecting, stops any recording that is running or paused and records the stop.

**Real-time hub.** The single "recording on/off" notification is replaced by four host-only commands (start, pause, resume, stop). Authorization uses the participant record the hub already keeps, so a caller who is not the Chủ trì of that room is ignored. After each accepted command the hub broadcasts the new recording state with the elapsed recorded time and a server timestamp, so every client can run a local display clock that does not drift. The payload sent to someone entering the room includes the current recording state.

**Browser recorder.** Pause and Resume use the recorder's own pause and resume, so the file stays continuous with no gap. Stop finalizes the file. The real duration patched into the file header is the active recorded time, excluding paused spans. Chunk upload continues unchanged while recording and stops while paused.

**Recording authorization and storage.** Starting a Bản ghi through the service is refused unless the caller is the Chủ trì of that Buổi học. Finishing a Bản ghi stores its duration and the time it was stopped. These are new nullable columns; Bản ghi created before this change keep both empty and the review page falls back to the video's own metadata. New columns are added to existing databases at startup using the project's existing column-patching convention for Sqlite and SqlServer.

**Diễn biến.** Four new event types are appended to the event type list for recording started, paused, resumed and stopped. Existing numeric values are never changed or reused, because they are stored in every existing row. Each recording event carries the identity of the Bản ghi it concerns, so the review page can place it.

**Seeking from the timeline.** The video position for an event is the event time minus the Bản ghi start time minus the total of paused spans that ended before that event, derived from that Bản ghi's own recording events. Bản ghi with no recording events (older ones) are treated as having no paused spans.

**History access rule.** One rule governs both visibility and access: a person may view a Buổi học's Lịch sử if they are its Chủ trì, an Admin, or have a "joined" entry in its Diễn biến. The same rule filters which ended Buổi học appear in lists, and the review and recording endpoints return 403 when it fails. Scheduled, live and cancelled Buổi học keep their current visibility.

**History list.** A history query returns the ended Buổi học the caller may view, newest first, with: title, Chủ trì, start and end, real duration (actual end minus actual start), number of distinct participants, number of Bản ghi and total size. It supports a text filter on title and Chủ trì name, a date range, and a "only mine" filter (Chủ trì or joined).

**Review query.** One query returns, for a Buổi học the caller may view, the summary (duration, participants, chat message count), the list of Bản ghi with durations and sizes, and the Diễn biến. Download is exposed to the Chủ trì and Admin only.

**Outer page.** The table is replaced by a live strip, an upcoming cards section with a countdown, and a Lịch sử tab. The code entry dialog is restyled and keeps its six-character behaviour. Create, edit, cancel and start-now behaviour is unchanged. Hard-coded button colours are removed in favour of the system's tokens. Who sees upcoming and live Buổi học is unchanged (everyone sees the schedule; only the join code is hidden).

**Màn hình xem trước.** It is a stage of the room page that runs before the real-time connection is opened. It lists devices, requests media with the chosen devices, shows a live preview and a microphone level meter, offers camera and microphone toggles with role-based defaults, and carries the choices into the room. Permission denial is handled as listener-only with an explanatory message. For a học viên it checks the Buổi học status on a short interval and enables Tham gia only when it is live. Entering the room is what opens the connection and, for the Chủ trì, starts the Buổi học, exactly as today.

**In-room layout.** The room keeps a dark, video-conference look built from the system's colour and typography tokens. Layout: main stage, collapsible right panel for chat and participants (becomes a bottom sheet on small screens), a labelled control bar, and a top bar showing title, status, elapsed Buổi học time, invitation code and recording state. Controls expose state through icon plus accessible name.

**Static recording files.** Bản ghi stay as files with unguessable names under the existing upload area; access control is enforced at the application endpoints that reveal their addresses. The files themselves are not moved behind an authenticated download. This is a known limitation, listed under Further Notes.

**Internationalisation.** Every new string is added to both the Vietnamese and English locale files; no hard-coded Vietnamese in markup or scripts.

## Testing Decisions

A good test exercises external behaviour through a service interface and asserts on observable outcomes (what is returned, what is refused, what is stored), never on how a class is built internally.

**The one seam** is the application layer's online-classroom services: the live-session service and the online-classroom service, run against an in-memory SQLite database with a fake current-user. The recording clock is exercised through the session service's behaviour (state and elapsed time reported after a sequence of commands), not by reaching into it separately, except that its pure transition rules may be tested directly because it has no dependencies.

Behaviours to cover through that seam:
- Only the Chủ trì can start a Bản ghi, and others are refused.
- Pause and Resume update the state; the elapsed time excludes paused spans; invalid transitions (resume when not paused, pause when idle) leave the state unchanged.
- Stop finalizes; starting again creates a new Bản ghi; ending the Buổi học while recording or paused finalizes.
- A late joiner receives the current state and elapsed time.
- Each recording transition writes the matching Diễn biến entry.
- Finishing a Bản ghi stores its duration.
- The history rule: Chủ trì, Admin and a person with a joined entry may view; everyone else is refused with 403 on the review and recording queries, and does not see the Buổi học in lists.
- The history list's filters (text, date range, only mine) and aggregates (duration, participant count, Bản ghi count and size).
- Seek offsets are computed correctly across paused spans.

There is no existing test project in the solution, so there is no prior art. This work introduces a new xUnit test project for the application layer, using the SQLite provider the project already depends on.

User-interface behaviour (Màn hình xem trước, the room layout, the outer page, the review page, theme and phone layouts) is verified in a headless browser against a scratch SQLite database, never the live database. Real camera, microphone, WebRTC and the browser recorder cannot be automated reliably; the end-to-end check of pause and resume producing a correct file is a documented manual checklist.

## Out of Scope

- Letting the Chủ trì delete their own Bản ghi or Buổi học (still Admin only).
- Switching camera or microphone after entering the room (devices are chosen in the Màn hình xem trước only).
- Letting anyone other than the Chủ trì record, co-hosts, or more than one simultaneous recorder.
- Serving recording files through an authenticated download so that the file address itself is protected.
- Transcripts, captions, search inside a video, or chat history shown to people joining late.
- Breakout rooms, hand-raising, polls, or a grid layout for many camera-on participants.
- Changing who can create a Buổi học or who sees the schedule of upcoming ones.
- A set of Oracle migrations; the project has none today.

## Further Notes

- **Design process.** All user-interface work is built with the design skills already installed (frontend design, UI/UX design intelligence, UI design system), reusing the system's existing tokens and the "ops" design language from the Tổng quan and Báo cáo pages, and is checked for light and dark themes, keyboard use, focus visibility, reduced motion, and 390px, 768px and 1280px widths.
- **Real database.** The development SqlServer database holds real accounts and data. The new nullable columns are added by the existing startup patching, so the first start of the new build alters the live tables. UI verification must always use a scratch SQLite copy.
- **Existing data.** Existing Bản ghi and Diễn biến keep working. Older Bản ghi have no stored duration and no recording events, so they show the duration from the video itself and seek with no paused spans.
- **Tracker.** Configuration says GitHub Issues, but the `gh` CLI is not installed on this machine, so this spec and its tickets are kept as local files under `.scratch/phong-hoc-online/`. They can be moved to GitHub later.
- **Known limitation.** Because recording files are static files with unguessable names, someone who is given a file address by a permitted viewer can open it without being checked again.
