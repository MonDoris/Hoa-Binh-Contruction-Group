MODEL 3D CHO TRANG MÔ PHỎNG (wwwroot/pages/simulation.html + wwwroot/js/sim-scene.js)
=======================================================================================

FILE ĐANG ĐƯỢC TRANG SỬ DỤNG
-----------------------------
  models/crane.glb              (1,8 MB)  -> cẩu tháp (cảnh công trường)
  models/worker.glb             (3,3 MB)  -> công nhân (dùng chung 1 file, nhân bản ra 8 vị trí, có animation đứng)
  models/character/staff.glb    (3,3 MB)  -> NHÂN VẬT người soạn bài tự đặt vào cảnh (xem mục riêng bên dưới)

Trang CHỈ tải hai file .glb này. Mọi thứ còn lại trong cảnh (khung nhà, giàn giáo, hàng rào, bãi vật liệu,
xe tải, biển báo, đường xe, vũng nước...) đều dựng bằng code trong sim-scene.js — cố ý như vậy, vì hình
dạng của chúng chính là nội dung bài học: giàn giáo phải THIẾU đúng thanh giằng chéo ở đúng nhịp, vật liệu
phải chất cao sát lối đi, lối người đi bộ phải cụt ngang đường xe. Model tải sẵn ngoài mạng không bao giờ
"sai" đúng chỗ mình cần.

Nếu thiếu một trong hai file .glb: trang VẪN CHẠY bình thường (cẩu tháp rơi về khối hình học tạm, công nhân
rơi về hình nhân dạng viên nang), chỉ ghi cảnh báo ở Console. Không bao giờ vỡ trang.

FILE GỐC (không được trang tải, giữ lại để dựng lại .glb khi cần)
------------------------------------------------------------------
  models/crane/source/grue-unwrap-v2.fbx   + models/crane/textures/*.png  (72 ảnh PBR kiểu Unity)
  models/worker.fbx                        (66 MB — bản Mixamo tải về, nhúng sẵn texture 4K)

  Hai file gốc này KHÔNG được tham chiếu ở đâu cả. Có thể chuyển ra ngoài wwwroot (hoặc xóa nếu đã lưu bản
  sao chỗ khác) mà trang vẫn chạy — riêng worker.fbx nặng 66 MB nên để trong thư mục web là lãng phí.

CÁCH DỰNG LẠI .glb TỪ FILE GỐC
-------------------------------
Cần Node.js. Các bước đã dùng để tạo hai file hiện tại:

  npm i fbx2gltf sharp @gltf-transform/core @gltf-transform/functions

  1) FBX -> glTF:
       npx FBX2glTF -i crane/source/grue-unwrap-v2.fbx -o crane -b --pbr-metallic-roughness --compute-normals missing
       npx FBX2glTF -i worker.fbx -o worker -b --pbr-metallic-roughness

  2) CẨU THÁP — file .fbx gốc KHÔNG tham chiếu texture nào (bản xuất từ 3ds Max, texture do Sketchfab đóng
     gói riêng), nên phải gắn tay bộ ảnh trong crane/textures/ vào từng vật liệu theo TÊN VẬT LIỆU
     (tên file có dạng "grue-unwrap-v2_<tên vật liệu>_<loại map>.png", và bị Sketchfab CẮT NGẮN cho vừa độ
     dài cố định — VD "..._grue-barrieres-support_Albe.png" — nên khi dò phải so tiền tố chứ đừng so cả tên).
     Đồng thời phải đổi map MetallicSmoothness kiểu Unity (kênh R = metallic, kênh A = smoothness) sang
     chuẩn glTF ORM (kênh G = roughness, kênh B = metallic = 1 - smoothness ở kênh G) — bỏ qua bước này thì
     cẩu sẽ ra bóng loáng như gương hoặc nhám xỉn hoàn toàn, không cách nào chỉnh bằng ánh sáng cho đúng.
     Cuối cùng thu ảnh về 256px (albedo/normal) và 128px (ORM), nén WebP: 17 MB PNG -> 1,8 MB.

  3) CÔNG NHÂN — bản Mixamo 53 MB có 4 ảnh 4K nhúng sẵn. Cần:
       - đặt lại baseColorFactor = [1,1,1,1] (FBX2glTF gán 0.8 xám làm nhân vật tối xỉn, riêng vật liệu tóc
         bị gán alpha = 0 nên tóc BIẾN MẤT hoàn toàn),
       - đổi alphaMode của tóc từ BLEND sang MASK,
       - metallicFactor = 0 (shading model Phong của Mixamo không có khái niệm kim loại),
       - nén texture về 1024px WebP: 53 MB -> 3,3 MB.

THAY MODEL KHÁC
----------------
Cứ ghi đè đúng tên crane.glb / worker.glb là xong, không cần sửa code — nhưng lưu ý:

  - CẨU THÁP: sim-scene.js tự đo hộp bao rồi hạ chân cẩu xuống chạm đất, và tự xoay cần (jib) chĩa về phía
    tải treo. Chỉ cần model có cần trục hướng theo trục -Z cục bộ. Tỉ lệ chỉnh ở SITE.craneScale (hiện 0.58
    cho ra cẩu cao ~18 m); tầm với của cần phải ≥ khoảng cách từ chân cẩu tới tải (~18 m).
  - CÔNG NHÂN: phải là nhân vật Mixamo (hoặc cùng quy ước đặt tên xương "mixamorig1:Head", "mixamorig1:Spine2")
    vì mũ bảo hộ và áo phản quang được gắn vào ĐÚNG HAI XƯƠNG đó để cử động theo animation. Xương tên khác
    thì nhân vật vẫn hiện nhưng không có mũ/áo — mà "có/không đội mũ bảo hộ" lại chính là một điểm nguy cơ
    trong bài học (hazard 4), nên phải kiểm tra lại nếu đổi model.
  - Ưu tiên .glb (đóng gói sẵn texture trong 1 file) và luôn nén texture trước khi bỏ vào — người học thường
    dùng mạng công ty, một file 50 MB sẽ làm trang treo ở màn hình chờ rất lâu.

GHI CÔNG (giấy phép)
---------------------
  - Model công trình trong tab "Mô hình công trình": "Under Construction Building" của nuralam018 trên
    Sketchfab — nhúng qua iframe, dòng ghi công hiện ngay dưới khung xem (bắt buộc theo giấy phép CC-BY).
  - Nhân vật công nhân: Mixamo (Adobe) — không yêu cầu ghi công khi dùng trong sản phẩm.
  - Cẩu tháp (grue-unwrap-v2): CẦN BỔ SUNG tên tác giả/giấy phép. Nếu model này lấy từ Sketchfab theo giấy
    phép CC-BY thì PHẢI thêm dòng ghi công tác giả vào biến CREDIT_HTML trong pages/simulation.html.


====================================================================================
NHÂN VẬT ĐẶT VÀO CẢNH (models/character/*.glb)
====================================================================================

Khác hai file ở trên (nằm cứng trong code của cảnh công trường), thư mục này phục vụ tính năng NGƯỜI SOẠN
BÀI TỰ ĐẶT NGƯỜI VÀO CẢNH: mở một mô phỏng dùng bối cảnh model 3D tự tải lên, bấm nút "Nhân vật", rồi bấm
thẳng lên sàn để đặt người và chọn tư thế. Xem wwwroot/js/sim-characters.js.

BỘ NHÂN VẬT — HAI KIỂU TỪ MỘT TỆP
  Tệp staff.glb là nhân vật Mixamo "Ch17", một CÔNG NHÂN XÂY DỰNG: có mũ bảo hộ và áo phản quang. Mixamo để
  mỗi món trang phục ở một mesh riêng (Ch17_Helmet, Ch17_Vest, Ch17_Shirt, Ch17_Pants...), nên chỉ cần ẩn
  mũ và áo phản quang đi là ra ngay một nhân viên văn phòng áo sơ mi. Nhờ vậy có hai bộ mà không cần thêm
  tệp nào:

      office   Nhân viên văn phòng   -> ẩn helmet + vest, nhuộm màu cho shirt
      worker   Công nhân công trường -> giữ nguyên, nhuộm màu cho vest

  Khai báo ở CHARACTER_SET_LIST trong wwwroot/js/sim-characters.js. Thêm một bộ dùng TỆP KHÁC thì trỏ
  trường `file` sang tên tệp mới (không kèm đuôi .glb) và thêm key đó vào CharacterSetList trong
  Controllers/ScenarioController.cs.

  MÀU TRANG PHỤC được nhân vào ảnh vân vải chứ không thay thế nó, nên giữ được nếp gấp và bóng đổ — đổi lại
  là màu quá tối sẽ ăn mòn hết chi tiết thành một mảng đen, vì vậy bảng màu chỉ để màu tầm trung. Người soạn
  bài không chọn màu thì hệ thống tự rải theo Id, để một phòng mười người không ra mười bản sao giống hệt.

TÌNH TRẠNG HIỆN TẠI
  staff.glb có HAI tư thế: "idle" (đứng thở) và "walk" (đi). Nguồn nằm ở Scripts/character-source/staff/.

  Tám tư thế còn lại — ngồi, uống, ăn, nói, gõ máy, chỉ tay, nghe điện thoại, đứng quan sát — vẫn chọn
  được trên giao diện nhưng khi dựng cảnh sẽ rơi về "idle", vì tệp chưa có đoạn tương ứng. Người vẫn hiện
  ra đầy đủ và vẫn thở, không có gì vỡ. Bổ sung thì làm theo mục dưới.

BỔ SUNG TƯ THẾ — 3 BƯỚC
  1. Vào mixamo.com (tài khoản Adobe miễn phí), tab Animations: mỗi tư thế tải một lần, Format
     **FBX Binary**, Skin = **Without Skin** (chỉ lấy chuyển động, không kèm lại thân người — kèm lại thì
     tệp phình lên gấp mấy lần mà nội dung thừa hoàn toàn).
     Đặt vào:  Scripts/character-source/staff/

     TÊN FILE CHÍNH LÀ TÊN TƯ THẾ và phải nằm trong POSE_LIST của wwwroot/js/sim-characters.js:
       idle.fbx  stand.fbx  sit.fbx  walk.fbx  drink.fbx  eat.fbx  talk.fbx  type.fbx  point.fbx  phone.fbx
     Animation tương ứng trên Mixamo: Idle / Standing / Sitting / Walking / Drinking / Eating / Talking /
     Typing / Pointing / Talking On Phone.

     KHÔNG cần tải lại nhân vật: script lấy chính wwwroot/models/character/staff.glb đang chạy làm người.
     Chỉ khi dựng một bộ HOÀN TOÀN MỚI mới cần thêm base.fbx (tab Characters, pose "T-pose") vào cùng
     thư mục.

  2. npm install          (package.json ở gốc dự án đã khai sẵn 4 gói cần thiết)

  3. node Scripts/build-character.mjs
     -> ghi đè wwwroot/models/character/staff.glb với đầy đủ các đoạn, mỗi đoạn mang đúng tên tư thế.

  BA CÁI BẪY script đã tự lo, đừng tự làm tay:
    - Tên xương hai bên LỆCH NHAU: tệp tải ở tab Animations dùng "mixamorig:Hips", còn tệp nhân vật dùng
      "mixamorig1:Hips". Cùng một bộ xương, khác mỗi cái tên.
    - Gộp xong thì đoạn animation vẫn điều khiển BỘ XƯƠNG ĐI KÈM TỆP TƯ THẾ, tức một bộ xương thứ hai
      không dính vào da thịt ai. Phải trỏ từng kênh về đúng xương của người rồi dọn bộ thừa đi.
    - Hai lỗi trên đều IM LẶNG: tệp ghi ra vẫn đủ tư thế, giao diện vẫn chọn được, mà người đứng chết. Dấu
      hiệu duy nhất là một xấp cảnh báo "THREE.PropertyBinding: No target node found for track" trong
      Console — mở Console kiểm sau mỗi lần dựng lại tệp nhân vật.

  KHÔNG phải sửa một dòng code nào sau đó: sim-characters.js dò đoạn animation theo tên lúc dựng cảnh.

THÊM BỘ NHÂN VẬT KHÁC (nam/nữ, công nhân/nhân viên văn phòng...)
  Làm y hệt với thư mục Scripts/character-source/<tên-bộ>/ rồi chạy
  "node Scripts/build-character.mjs <tên-bộ>". File ra sẽ là models/character/<tên-bộ>.glb. Để người soạn
  bài chọn được bộ đó thì thêm lựa chọn vào ô "bộ nhân vật" ở màn đặt nhân vật (hiện đang cố định "staff").

CỠ NGƯỜI
  Không cần chỉnh tỉ lệ trong file: sim-characters.js tự đo hộp bao rồi quy mọi nhân vật về cùng một chiều
  cao chuẩn, nên file dựng theo mét hay centimet đều dùng được. Cỡ người trong từng cảnh do người soạn bài
  kéo thanh trượt (lưu ở ScenarioScene.CharacterScale) — bắt buộc phải có vì mọi model tải lên đều bị co về
  cùng một hộp bao 10 đơn vị bất kể kích thước thật.
