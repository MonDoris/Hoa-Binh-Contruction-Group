# HB E-Learning

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![EF Core](https://img.shields.io/badge/EF%20Core-10-512BD4)
![Realtime](https://img.shields.io/badge/Realtime-SignalR-512BD4)
![AI](https://img.shields.io/badge/AI-Ollama%20(local%20LLM)-000000)
![Status](https://img.shields.io/badge/status-in%20development-yellow)

**Nền tảng E-Learning quản lý toàn bộ vòng đời đào tạo nội bộ doanh nghiệp**: xây dựng khóa học theo phòng ban/vị trí, lộ trình học tập, kiểm tra đánh giá, mô phỏng tình huống thực tế, lớp học trực tuyến, trợ lý AI và cấp chứng nhận xác thực bằng QR Code.

*A full-stack internal corporate-training (e-learning) platform built with ASP.NET Core, EF Core, SignalR, WebRTC and a local AI assistant (Ollama).*

## Mục lục

- [Giới thiệu](#giới-thiệu)
- [Tính năng chính](#tính-năng-chính)
- [Vai trò & phân quyền](#vai-trò--phân-quyền)
- [Kiến trúc hệ thống](#kiến-trúc-hệ-thống)
- [Công nghệ sử dụng](#công-nghệ-sử-dụng)
- [Cấu trúc thư mục](#cấu-trúc-thư-mục)
- [Bắt đầu nhanh](#bắt-đầu-nhanh)
- [Cấu hình chi tiết](#cấu-hình-chi-tiết)
- [Tài liệu liên quan](#tài-liệu-liên-quan)
- [Đóng góp](#đóng-góp)
- [Giấy phép](#giấy-phép)

## Giới thiệu

HB E-Learning (Hòa Bình E-Learning) là hệ thống E-Learning phục vụ **đào tạo nội bộ doanh nghiệp**. Người phụ trách đào tạo (Trưởng Nhóm) xây dựng khóa học, bài học và lộ trình học tập phù hợp với từng phòng ban/vị trí công việc; nội dung đi qua một luồng **duyệt theo cấp quản lý** trước khi công bố. Học viên học theo lộ trình, làm bài kiểm tra, được hệ thống ghi nhận tiến độ và cấp chứng nhận khi hoàn thành.

Ngoài luồng đào tạo cơ bản, hệ thống còn có: mô phỏng tình huống công việc thực tế (3D), lớp học trực tuyến qua video call, trợ lý AI chạy nội bộ (hỏi-đáp tài liệu, soạn nội dung, sinh câu hỏi, dịch giao diện), gamification (điểm thưởng, xếp hạng, cửa hàng đổi quà) và xác thực chứng nhận bằng QR Code.

## Tính năng chính

| Nhóm | Tính năng |
|---|---|
| **Người dùng & tổ chức** | Đăng nhập JWT, quản lý người dùng, phòng ban, vị trí, vai trò & phân quyền động theo chức năng |
| **Đào tạo** | Khóa học, bài học, lộ trình học tập, quiz/kiểm tra, ghi danh (enrollment), theo dõi tiến độ, ghi chú bài học, học qua video |
| **Duyệt nội dung** | Luồng phê duyệt khóa học tự cấu hình theo từng phòng ban (Trưởng Nhóm → Trưởng phòng/Giám Đốc), ủy quyền duyệt (delegate) khi vắng mặt |
| **Lớp học trực tuyến** | Phòng học online real-time qua SignalR + WebRTC, ghi hình buổi học, kết nối xuyên NAT qua TURN server tự triển khai |
| **Trợ lý AI (chạy nội bộ)** | Hỏi-đáp tài liệu đào tạo (RAG), AI Tutor, gợi ý trả lời nhanh trong chat, soạn nội dung bài học, sinh câu hỏi kiểm tra, dịch giao diện đa ngôn ngữ |
| **Mô phỏng tình huống** | Kịch bản tình huống công việc dạng tương tác (nhân vật, thoại, lựa chọn) với asset 3D |
| **Gamification** | Tích điểm, bảng xếp hạng theo phòng ban/công ty, cửa hàng đổi thưởng, mã voucher, nhiệm vụ tuần |
| **Chứng nhận** | Cấp chứng nhận hoàn thành khóa học, xác thực bằng QR Code |
| **Giao tiếp** | Chat nội bộ real-time, thông báo, nhắc nhở học tập định kỳ |
| **Nhập liệu & báo cáo** | Import hàng loạt bài học/câu hỏi từ Excel/Word/PDF (có file mẫu), dashboard & báo cáo thống kê đào tạo |
| **Đa ngôn ngữ** | Quản lý từ điển dịch cho giao diện, hỗ trợ dịch tự động bằng AI |

## Vai trò & phân quyền

Hệ thống phân quyền theo **cấp bậc quản lý** (khớp cơ cấu tổ chức thực tế) kết hợp quyền động theo từng chức năng:

| Vai trò | Vai trò trong hệ thống |
|---|---|
| **Admin** | Quản trị hệ thống: người dùng, phòng ban, vai trò, phân quyền, xếp hạng — không tham gia soạn/duyệt nội dung đào tạo |
| **Tổng Giám Đốc** | Xem báo cáo & xếp hạng xuyên suốt toàn công ty |
| **Giám Đốc** | Duyệt nội dung do Trưởng phòng trình lên, quản lý phòng ban phụ trách |
| **Trưởng phòng** | Duyệt nội dung do Trưởng Nhóm tạo, báo cáo theo phòng ban |
| **Trưởng Nhóm** | Soạn nội dung đào tạo: khóa học, bài học, kiểm tra, mô phỏng, lộ trình; phân công học viên |
| **Học viên** | Tham gia học theo lộ trình, làm bài kiểm tra, xem tiến độ & chứng nhận cá nhân |

Admin có thể tạo thêm vai trò tùy biến (ví dụ "Trưởng phòng IT") và gán quyền theo từng chức năng ở màn hình **Vai trò**.

## Kiến trúc hệ thống

Giải pháp tổ chức theo **Clean Architecture**, 4 project xếp từ trong ra ngoài — mũi tên là chiều được phép phụ thuộc, do trình biên dịch cưỡng chế (không chỉ là quy ước):

```
Domain  <--  Application  <--  Infrastructure
                  ^                  ^
                  |                  |
                  +------- API ------+   (API chỉ chạm Infrastructure trong Program.cs)
```

- **Domain** — Entity, Enum, hằng số nghiệp vụ; không phụ thuộc gì.
- **Application** — Use case (`Features/*`), DTO, và các "cổng" (interface) ra bên ngoài (`Common/Interfaces`); chỉ nói chuyện với database qua `IAppDbContext`, không phụ thuộc ASP.NET Core.
- **Infrastructure** — Hiện thực các cổng: EF Core, JWT, SMTP/SMS/Zalo, Ollama, đọc/ghi Excel-Word-PDF.
- **API** — Controller, SignalR Hub, Swagger, middleware, và frontend tĩnh trong `wwwroot/`.

Xem giải thích đầy đủ (quy tắc từng lớp, luồng một request, thêm tính năng thì sửa ở đâu) tại [`HbELearning/KIEN-TRUC.md`](HbELearning/KIEN-TRUC.md).

## Công nghệ sử dụng

| Thành phần | Công nghệ |
|---|---|
| Backend | ASP.NET Core Web API (.NET 10) theo Clean Architecture |
| Cơ sở dữ liệu | EF Core 10 — chọn 1 trong 3 provider: **SQLite** (mặc định, dev), **SQL Server**, **Oracle** |
| Xác thực & phân quyền | JWT Bearer, BCrypt (hash mật khẩu), RBAC theo vai trò + quyền động theo chức năng |
| Realtime | SignalR — chat nội bộ (`/hubs/chat`), lớp học online (`/hubs/classroom`) |
| Video call | WebRTC + STUN/TURN tự triển khai (coturn, xem `deploy/coturn/`) |
| AI (chạy nội bộ, offline) | [Ollama](https://ollama.com) — `nomic-embed-text` (embedding), `qwen2.5:7b`/`qwen2.5:3b` (chat, hỏi-đáp, soạn nội dung, dịch) |
| Xử lý tài liệu | ClosedXML (Excel), DocumentFormat.OpenXml (Word), PdfPig (trích xuất PDF), QRCoder (QR chứng nhận) |
| Thông báo OTP | SMTP (Gmail), eSMS.vn (SMS Brandname), Zalo ZNS — 3 kênh song song cho quên mật khẩu |
| Frontend | HTML/CSS/JS thuần, PWA (service worker + manifest), đa ngôn ngữ (`data-i18n`), Phosphor Icons (vendor cục bộ, không phụ thuộc CDN) |
| Công cụ dựng asset 3D | Node.js (`Scripts/`) — `@gltf-transform`, `fbx2gltf`, `sharp` |
| API Docs | Swagger / Swashbuckle tại `/swagger` (chỉ bật ở môi trường Development) |

## Cấu trúc thư mục

```
HB E-Learning/
├── HbELearning/                 # Solution .NET chính
│   ├── Domain/                   # Entity, Enum, hằng số — không phụ thuộc gì
│   ├── Application/               # Use case (Features/*), DTO, interface cổng ra ngoài
│   ├── Infrastructure/            # EF Core, JWT, SMTP/SMS/Zalo, Ollama, đọc/ghi file
│   ├── API/                       # Controller, SignalR Hub, Program.cs, wwwroot (frontend)
│   ├── Scripts/                   # Pipeline dựng asset 3D + sơ đồ ERD (Mermaid)
│   ├── KIEN-TRUC.md               # Giải thích chi tiết Clean Architecture
│   └── HbELearning.sln
├── deploy/coturn/                # Hướng dẫn & cấu hình triển khai TURN server (video call)
├── Docs/                          # Tài liệu mô tả đề bài / thiết kế ban đầu
└── Mau-Import/                    # File mẫu Excel để import bài học/câu hỏi
```

## Bắt đầu nhanh

### Yêu cầu

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- (Tùy chọn) [Ollama](https://ollama.com) — nếu muốn dùng các tính năng AI
- (Tùy chọn) Node.js ≥ 18 — nếu cần chạy lại pipeline dựng asset 3D trong `Scripts/`
- (Tùy chọn) SQL Server hoặc Oracle — mặc định dùng SQLite, tự tạo file & seed dữ liệu ở lần chạy đầu, không cần cài thêm gì

### Cài đặt

```bash
git clone <repo-url>
cd "HB E-Learning/HbELearning"
dotnet restore
```

### Cấu hình

Mở `API/appsettings.json` và điền các mục cần dùng (chi tiết ở phần [Cấu hình chi tiết](#cấu-hình-chi-tiết) bên dưới). Ở môi trường dev, để nguyên `DatabaseProvider = "Sqlite"` là chạy được ngay, không cần cấu hình thêm.

> ⚠️ **Lưu ý bảo mật:** không commit giá trị thật của `Jwt:Key`, `ConnectionStrings`, `Smtp:Password`, `Esms:*`, `ZaloZns:SecretKey`... lên Git. Khi phát triển nên dùng [`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets); khi triển khai thật nên dùng biến môi trường hoặc secret manager, đồng thời tách các giá trị thật này ra khỏi file cấu hình được commit.

### Chạy ứng dụng

```bash
dotnet run --project API
```

- Giao diện web: `https://localhost:<port>` (frontend tĩnh trong `API/wwwroot`)
- Swagger UI (chỉ ở Development): `https://localhost:<port>/swagger`

Cơ sở dữ liệu và dữ liệu nền (vai trò, quyền...) được tự động khởi tạo/seed ở lần chạy đầu tiên — xem `Infrastructure/Persistence/Initialization/DatabaseInitializer.cs`.

## Cấu hình chi tiết

| Mục trong `appsettings.json` | Ý nghĩa |
|---|---|
| `DatabaseProvider` | `Sqlite` \| `SqlServer` \| `Oracle` — đổi provider database, kèm chuỗi kết nối tương ứng trong `ConnectionStrings` |
| `Jwt` | `Key`, `Issuer`, `Audience`, `ExpireMinutes` cho access token |
| `Smtp` / `Esms` / `ZaloZns` | 3 kênh gửi OTP quên mật khẩu (Email/SMS Brandname/Zalo ZNS) — chỉ cần cấu hình kênh nào định dùng |
| `Turn` | Thông tin TURN server tự triển khai cho video call xuyên NAT — để trống `Host` để tắt (chỉ gọi được cùng mạng LAN/VPN); xem hướng dẫn triển khai đầy đủ tại [`deploy/coturn/README.md`](deploy/coturn/README.md) |
| `Ollama` | `BaseUrl`, `EmbeddingModel`, `ChatModel`, `AnswerModel`... cho các tính năng AI. Cài Ollama rồi chạy: `ollama pull nomic-embed-text && ollama pull qwen2.5:7b && ollama pull qwen2.5:3b` |
| `Upload` | Giới hạn dung lượng & định dạng file được phép tải lên |

## Tài liệu liên quan

- [`HbELearning/KIEN-TRUC.md`](HbELearning/KIEN-TRUC.md) — chi tiết Clean Architecture, luồng xử lý request, quy tắc thêm tính năng mới
- [`HbELearning/Scripts/erd.mermaid`](HbELearning/Scripts/erd.mermaid) — sơ đồ quan hệ dữ liệu (ERD)
- [`deploy/coturn/README.md`](deploy/coturn/README.md) — hướng dẫn triển khai TURN server cho lớp học online
- [`Docs/`](Docs) — tài liệu mô tả đề bài và thiết kế ban đầu của dự án

## Đóng góp

Pull request và issue báo lỗi/đề xuất tính năng đều được hoan nghênh. Khi thêm tính năng mới, vui lòng tuân theo ranh giới các lớp mô tả trong `KIEN-TRUC.md` (đặc biệt: Application không được phụ thuộc Infrastructure hay ASP.NET Core).

## Giấy phép

Dự án hiện chưa gắn giấy phép mã nguồn mở cụ thể. Nếu công khai trên GitHub, cân nhắc bổ sung file `LICENSE` (MIT, Apache-2.0...) phù hợp với mục đích sử dụng.
