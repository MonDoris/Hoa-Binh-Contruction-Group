// File: wwwroot/js/sim-hr-scene.js
// ============================================================================================
// BỐI CẢNH 3D: VĂN PHÒNG PHÒNG NHÂN SỰ  (SceneKey = "hr-office")
//
// Dùng cho kịch bản "Quy trình xử lý hồ sơ ứng viên phỏng vấn và nhân viên mới". Người học nhập vai
// chuyên viên nhân sự, đi lần lượt qua 8 khu vực của tầng làm việc — mỗi khu vực là một bước của quy
// trình, gắn với một ScenarioNode trong CSDL (xem Data/DbSeeder.cs).
//
// KHÁC GÌ SO VỚI CẢNH CÔNG TRƯỜNG (js/sim-scene.js)
// -------------------------------------------------
//  * Không gian TRONG NHÀ: không có bầu trời HDR làm nguồn sáng chính, mà dùng RoomEnvironment của
//    Three.js sinh env map nội thất (trần sáng, tường sáng) — đây mới là thứ khiến bàn gỗ, màn hình,
//    kính ngăn phòng có phản chiếu đúng kiểu văn phòng thay vì đen kịt.
//  * KHÔNG DỰNG TRẦN. Người học nhìn từ trên xuống theo kiểu "mô hình mặt bằng", nên trần sẽ che hết
//    mọi thứ. Ánh sáng trần được gợi ý bằng các tấm phát sáng mỏng lơ lửng ở cao độ trần.
//  * Toàn bộ dựng bằng code, không tải model ngoài. Lý do giống cảnh công trường: HÌNH DẠNG CHÍNH LÀ
//    NỘI DUNG BÀI HỌC — tủ hồ sơ phải hé mở đúng ngăn, khay máy in phải còn giấy bỏ quên, bàn nhân
//    viên mới phải trống trơn chưa có thiết bị. Model tải sẵn ngoài mạng không bao giờ "sai" đúng chỗ.
//
// BỐ CỤC MẶT BẰNG (1 đơn vị = 1 mét, cửa vào ở phía +Z)
//
//        -Z  (phía trong)
//     ┌──────────────────────┬───────────────────────┐
//     │  PHÒNG PHỎNG VẤN     │   PHÒNG HỌP / OFFER   │   z = -11 .. -3.5
//     │  (kính, node 4)      │   (kính, node 5)      │
//     ├──────────────────────┴───────────────────────┤
//     │              hành lang + cây xanh            │   z = -3.5 .. 0.5
//     ├─────────────┬──────────────┬─────────────────┤
//     │ BÀN SÀNG LỌC│ BÀN HỒ SƠ NV │ BÀN HỢP ĐỒNG    │   z = 0.5 .. 4
//     │  (node 2)   │  MỚI (node 6)│ & BHXH (node 7) │
//     ├─────────────┼──────────────┼─────────────────┤
//     │ QUẦY LỄ TÂN │  KHU ONBOARD │ MÁY IN & TỦ HỒ  │   z = 4.5 .. 11
//     │  (node 1)   │  ING (node 8)│ SƠ (node 3)     │
//     └─────────────┴──────────────┴─────────────────┘
//        +Z  (lối vào)
// ============================================================================================

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';

import { makeSurface, canvasTexture, box, texBox, tube, contactShadow, createDirector, createViewerControl } from '/js/sim-kit.js';

// ==================== HẰNG SỐ BỐ CỤC ====================
// Các tọa độ dưới đây phải khớp PositionX/Y/Z của ScenarioNode trong CSDL (Data/DbSeeder.cs) — marker
// được chiếu lên màn hình theo đúng tọa độ đó, dời đồ đạc mà quên dời theo thì marker chỉ vào chỗ trống.
const OFFICE = {
    minX: -15, maxX: 15,
    minZ: -12, maxZ: 12,
    wallHeight: 2.9,
    reception: { x: -8.5, z: 7.0 },       // node 1 — tiếp nhận hồ sơ
    screening: { x: -7.5, z: 2.5 },       // node 2 — sàng lọc CV
    printer:   { x: 12.0, z: 7.5 },       // node 3 — máy in & tủ hồ sơ (bảo mật dữ liệu)
    interview: { x: -8.0, z: -6.5 },      // node 4 — phòng phỏng vấn
    meeting:   { x: 6.0,  z: -6.5 },      // node 5 — phòng họp / thư mời nhận việc
    newFile:   { x: 7.0,  z: 2.5 },       // node 6 — lập hồ sơ nhân viên mới
    contract:  { x: 11.5, z: 1.0 },       // node 7 — hợp đồng thử việc & BHXH
    onboard:   { x: 2.5,  z: 7.5 },       // node 8 — chỗ ngồi nhân viên mới
    roomSplitZ: -3.5,                     // vách ngăn hai phòng họp với khu mở
};

// Bảng màu văn phòng: xanh dương công ty + gỗ ấm + xám trung tính. Giữ tập màu hẹp để cảnh trông
// "được thiết kế" chứ không phải mỗi món một màu.
const TONE = {
    brand: 0x1d4ed8,
    brandSoft: 0x3b82f6,
    wood: 0xb98b5e,
    woodDark: 0x8a6440,
    panel: 0xe8eaee,
    metal: 0x9aa3ad,
    chair: 0x2f3743,
    chairAccent: 0x475569,
    paper: 0xfdfdfb,
    plant: 0x2f7a4a,
};

// ==================== TEXTURE RIÊNG CỦA VĂN PHÒNG ====================

/** Thảm trải sàn dạng tấm (carpet tile): nhiễu hạt mịn + đường ghép tấm mờ mỗi 60cm. Sàn là bề mặt
 *  lớn nhất trong cảnh, nếu để một màu phẳng thì cả căn phòng lập tức trông như bản dựng nháp. */
function carpetSurface() {
    const surface = makeSurface({
        size: 256,
        octaveList: [[64, 1], [128, 0.6], [32, 0.35]],
        darkColor: [46, 54, 66],
        lightColor: [86, 96, 110],
        repeat: 26,
        normalStrength: 14,
        roughLow: 0.92, roughHigh: 1.0,
        speckle: 0.06,
    });
    return surface;
}

/** Sàn gỗ công nghiệp cho hai phòng họp — ván dài, vân dọc, khe ghép sẫm màu. */
function woodFloorTexture(repeat = [4, 3]) {
    return canvasTexture(512, 512, (ctx, w, h) => {
        const plank = h / 8;
        for (let r = 0; r < 8; r++) {
            const base = 150 + Math.random() * 30;
            ctx.fillStyle = `rgb(${base | 0}, ${(base * 0.72) | 0}, ${(base * 0.48) | 0})`;
            ctx.fillRect(0, r * plank, w, plank);
            // vân gỗ: các đường mảnh sẫm chạy dọc ván
            for (let i = 0; i < 26; i++) {
                ctx.strokeStyle = `rgba(90, 60, 34, ${0.05 + Math.random() * 0.12})`;
                ctx.lineWidth = 0.6 + Math.random();
                const y = r * plank + Math.random() * plank;
                ctx.beginPath();
                ctx.moveTo(0, y);
                ctx.bezierCurveTo(w * 0.3, y + 3, w * 0.7, y - 3, w, y);
                ctx.stroke();
            }
            ctx.fillStyle = 'rgba(60, 40, 22, .5)';   // khe ghép
            ctx.fillRect(0, r * plank, w, 1.5);
        }
    }, { repeat });
}

/** Mặt bàn gỗ sáng — cùng thuật toán vân nhưng thớ ngang và sáng hơn sàn. */
function deskTopTexture() {
    return canvasTexture(256, 256, (ctx, w, h) => {
        ctx.fillStyle = '#c79a6d';
        ctx.fillRect(0, 0, w, h);
        for (let i = 0; i < 90; i++) {
            ctx.strokeStyle = `rgba(120, 82, 48, ${0.04 + Math.random() * 0.1})`;
            ctx.lineWidth = 0.5 + Math.random() * 1.4;
            const x = Math.random() * w;
            ctx.beginPath();
            ctx.moveTo(x, 0);
            ctx.bezierCurveTo(x + 8, h * 0.35, x - 8, h * 0.7, x + 2, h);
            ctx.stroke();
        }
    });
}

/** Biển/bảng chữ tiếng Việt vẽ trên canvas. Dùng cho bảng tên phòng, tường logo, nhãn tủ hồ sơ —
 *  chi tiết nhỏ nhưng là thứ khiến người học đọc được ngay mình đang đứng ở khu vực nào. */
function labelTexture(lineList, {
    bg = '#ffffff', fg = '#1e293b', accent = null, size = [512, 256], pad = 26, weight = 'bold',
} = {}) {
    const [w, h] = size;
    return canvasTexture(w, h, (ctx) => {
        ctx.fillStyle = bg;
        ctx.fillRect(0, 0, w, h);
        if (accent) {
            ctx.fillStyle = accent;
            ctx.fillRect(0, 0, w, 12);
        }
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        const lineHeight = (h - pad * 2) / lineList.length;
        lineList.forEach((line, i) => {
            const fontSize = Math.min(lineHeight * 0.62, (w - pad * 2) / (line.length * 0.55));
            ctx.fillStyle = line.startsWith('#') ? '#64748b' : fg;
            ctx.font = `${weight} ${Math.max(14, fontSize) | 0}px "Segoe UI", Arial, sans-serif`;
            ctx.fillText(line.replace(/^#/, ''), w / 2, pad + lineHeight * (i + 0.5));
        });
    });
}

/** Màn hình máy tính đang mở danh sách hồ sơ ứng viên — vẽ giả giao diện bảng dữ liệu. Nhìn từ xa chỉ
 *  là một mảng sáng xanh, nhưng khi camera lia lại gần bàn sàng lọc thì đọc được "đang làm gì ở đây". */
function screenTexture(title) {
    return canvasTexture(512, 320, (ctx, w, h) => {
        ctx.fillStyle = '#0f172a'; ctx.fillRect(0, 0, w, h);
        ctx.fillStyle = '#1d4ed8'; ctx.fillRect(0, 0, w, 44);
        ctx.fillStyle = '#ffffff';
        ctx.font = 'bold 22px "Segoe UI", Arial, sans-serif';
        ctx.textBaseline = 'middle';
        ctx.fillText(title, 18, 23);
        for (let r = 0; r < 8; r++) {
            const y = 62 + r * 31;
            ctx.fillStyle = r % 2 ? '#111c33' : '#16233d';
            ctx.fillRect(12, y, w - 24, 26);
            ctx.fillStyle = '#94a3b8';
            ctx.fillRect(22, y + 10, 90 + Math.random() * 70, 6);
            ctx.fillRect(210, y + 10, 60 + Math.random() * 50, 6);
            ctx.fillStyle = r < 3 ? '#22c55e' : '#f59e0b';
            ctx.fillRect(w - 90, y + 8, 54, 10);
        }
    });
}

/** Tờ giấy A4 có chữ — dùng cho CV trên bàn, thư mời nhận việc, hợp đồng. */
function documentTexture({ heading = '', headingColor = '#1d4ed8', hasPhoto = false, stampText = null } = {}) {
    return canvasTexture(256, 362, (ctx, w, h) => {
        ctx.fillStyle = '#fdfdfb'; ctx.fillRect(0, 0, w, h);
        if (hasPhoto) {
            ctx.fillStyle = '#cbd5e1';
            ctx.fillRect(w - 74, 22, 52, 66);
        }
        ctx.fillStyle = headingColor;
        ctx.fillRect(22, 26, hasPhoto ? 120 : 150, 13);
        ctx.fillStyle = '#94a3b8';
        ctx.fillRect(22, 48, 96, 7);
        if (heading) {
            ctx.fillStyle = headingColor;
            ctx.font = 'bold 15px "Segoe UI", Arial, sans-serif';
            ctx.fillText(heading, 22, 78);
        }
        for (let i = 0; i < 14; i++) {
            ctx.fillStyle = '#d5dbe3';
            ctx.fillRect(22, 100 + i * 16, (w - 44) * (0.45 + Math.random() * 0.55), 6);
        }
        if (stampText) {
            ctx.save();
            ctx.translate(w * 0.66, h * 0.8);
            ctx.rotate(-0.22);
            ctx.strokeStyle = '#dc2626'; ctx.lineWidth = 3;
            ctx.strokeRect(-58, -20, 116, 40);
            ctx.fillStyle = '#dc2626';
            ctx.font = 'bold 17px "Segoe UI", Arial, sans-serif';
            ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
            ctx.fillText(stampText, 0, 1);
            ctx.restore();
        }
    });
}

/** Đường chân trời thành phố nhìn qua vách kính — vẽ một dải nhà cao tầng mờ dần lên nền trời. Không có
 *  nó thì bên ngoài cửa sổ là một khoảng trống xám, và cả căn phòng trông như đang lơ lửng trong hư vô. */
function skylineTexture() {
    return canvasTexture(1024, 256, (ctx, w, h) => {
        const sky = ctx.createLinearGradient(0, 0, 0, h);
        sky.addColorStop(0, '#a9c9ee');
        sky.addColorStop(0.62, '#d7e6f6');
        sky.addColorStop(1, '#eaf1f8');
        ctx.fillStyle = sky; ctx.fillRect(0, 0, w, h);
        // 3 lớp nhà, lớp xa mờ hơn — tạo chiều sâu mà không tốn thêm hình học nào
        const layerList = [
            { tint: 'rgba(148, 172, 200, .45)', minH: 40, maxH: 95 },
            { tint: 'rgba(120, 146, 180, .62)', minH: 60, maxH: 130 },
            { tint: 'rgba(92, 118, 152, .8)', minH: 80, maxH: 168 },
        ];
        for (const layer of layerList) {
            let x = -20;
            while (x < w + 20) {
                const bw = 26 + Math.random() * 54;
                const bh = layer.minH + Math.random() * (layer.maxH - layer.minH);
                ctx.fillStyle = layer.tint;
                ctx.fillRect(x, h - bh, bw, bh);
                x += bw + 6 + Math.random() * 14;
            }
        }
    }, { repeat: [1, 1] });
}

// ==================== TIỆN ÍCH DỰNG NỘI THẤT ====================

/** Bàn làm việc: mặt bàn + hộc tủ + 4 chân thép. Trả về Group để đặt/xoay nguyên cụm. */
function makeDesk(material, { width = 1.7, depth = 0.8, height = 0.74, drawer = true } = {}) {
    const group = new THREE.Group();
    group.add(texBox(material.deskTop, width, 0.05, depth, 0, height, 0, 0, 1.2));

    const legInset = 0.09;
    for (const sx of [-1, 1]) {
        for (const sz of [-1, 1]) {
            group.add(tube(material.metal, 0.022, height,
                sx * (width / 2 - legInset), height / 2, sz * (depth / 2 - legInset)));
        }
    }
    if (drawer) {
        const cabinetWidth = 0.42;
        group.add(box(material.panelLight, cabinetWidth, height - 0.1, depth - 0.12,
            width / 2 - cabinetWidth / 2 - 0.06, (height - 0.1) / 2, 0));
        for (let i = 0; i < 3; i++) {
            group.add(box(material.metal, 0.16, 0.012, 0.02,
                width / 2 - cabinetWidth / 2 - 0.06, 0.18 + i * 0.2, (depth - 0.12) / 2 + 0.01));
        }
    }
    return group;
}

/** Ghế văn phòng có bánh xe: đế 5 chân, trụ, đệm ngồi, lưng tựa. */
function makeOfficeChair(material, { rotY = 0, accent = TONE.chair } = {}) {
    const group = new THREE.Group();
    const seatMaterial = new THREE.MeshStandardMaterial({ color: accent, roughness: 0.85 });

    for (let i = 0; i < 5; i++) {
        const angle = (i / 5) * Math.PI * 2;
        const leg = tube(material.metalDark, 0.022, 0.3, Math.cos(angle) * 0.15, 0.05, Math.sin(angle) * 0.15);
        leg.rotation.z = Math.cos(angle) * 0.9;
        leg.rotation.x = -Math.sin(angle) * 0.9;
        group.add(leg);
        group.add(tube(material.metalDark, 0.028, 0.05, Math.cos(angle) * 0.28, 0.03, Math.sin(angle) * 0.28));
    }
    group.add(tube(material.metalDark, 0.035, 0.28, 0, 0.28, 0));
    group.add(box(seatMaterial, 0.48, 0.09, 0.46, 0, 0.45, 0));
    const back = box(seatMaterial, 0.46, 0.52, 0.08, 0, 0.74, -0.22);
    back.rotation.x = -0.14;
    group.add(back);
    group.rotation.y = rotY;
    return group;
}

/** Ghế phòng họp (4 chân, không bánh xe) — nhẹ hơn ghế xoay cả về hình lẫn số đa giác. */
function makeMeetingChair(material, { rotY = 0, accent = TONE.chairAccent } = {}) {
    const group = new THREE.Group();
    const seatMaterial = new THREE.MeshStandardMaterial({ color: accent, roughness: 0.88 });
    for (const sx of [-1, 1]) {
        for (const sz of [-1, 1]) {
            group.add(tube(material.metalDark, 0.018, 0.44, sx * 0.19, 0.22, sz * 0.19));
        }
    }
    group.add(box(seatMaterial, 0.46, 0.07, 0.44, 0, 0.47, 0));
    const back = box(seatMaterial, 0.44, 0.44, 0.06, 0, 0.72, -0.2);
    back.rotation.x = -0.1;
    group.add(back);
    group.rotation.y = rotY;
    return group;
}

/** Màn hình máy tính + chân đế + bàn phím. */
function makeMonitor(material, screenMap, { rotY = 0 } = {}) {
    const group = new THREE.Group();
    group.add(box(material.metalDark, 0.24, 0.02, 0.16, 0, 0.01, 0.02));
    group.add(tube(material.metalDark, 0.02, 0.18, 0, 0.1, 0.02));
    group.add(box(material.metalDark, 0.56, 0.35, 0.02, 0, 0.32, 0));
    const screen = new THREE.Mesh(
        new THREE.PlaneGeometry(0.52, 0.31),
        new THREE.MeshBasicMaterial({ map: screenMap })   // tự phát sáng: màn hình không phụ thuộc đèn phòng
    );
    screen.position.set(0, 0.32, 0.012);
    group.add(screen);
    group.add(box(material.panelLight, 0.42, 0.015, 0.14, 0, 0.008, 0.28));
    group.rotation.y = rotY;
    return group;
}

/** Chồng giấy/hồ sơ đặt trên mặt phẳng — mỗi tờ lệch nhau vài độ cho ra vẻ xếp tay. */
function makePaperStack(count, documentMap, { spread = 0.035, sheet = [0.21, 0.297] } = {}) {
    const group = new THREE.Group();
    const geo = new THREE.PlaneGeometry(sheet[0], sheet[1]);
    const mat = new THREE.MeshStandardMaterial({ map: documentMap, roughness: 0.95, side: THREE.DoubleSide });
    for (let i = 0; i < count; i++) {
        const page = new THREE.Mesh(geo, mat);
        page.rotation.x = -Math.PI / 2;
        page.rotation.z = (Math.random() - 0.5) * spread * 8;
        page.position.set((Math.random() - 0.5) * spread, i * 0.0016, (Math.random() - 0.5) * spread);
        page.castShadow = false;
        page.receiveShadow = true;
        group.add(page);
    }
    return group;
}

/** Khay đựng hồ sơ 2-3 tầng trên bàn. */
function makeDocumentTray(material, tiers, documentMap) {
    const group = new THREE.Group();
    for (let i = 0; i < tiers; i++) {
        const y = i * 0.11;
        group.add(box(material.metalDark, 0.3, 0.008, 0.24, 0, y + 0.02, 0));
        group.add(box(material.metalDark, 0.3, 0.045, 0.008, 0, y + 0.045, -0.12));
        for (const sx of [-1, 1]) {
            group.add(tube(material.metalDark, 0.008, 0.1, sx * 0.14, y + 0.07, -0.11));
        }
        const stack = makePaperStack(4 + i * 2, documentMap, { sheet: [0.2, 0.22] });
        stack.position.y = y + 0.026;
        group.add(stack);
    }
    return group;
}

/** Tủ hồ sơ 4 ngăn kéo. `openDrawer` = chỉ số ngăn bị kéo ra (null = đóng hết) — dùng cho tình huống
 *  "tủ hồ sơ không khóa" ở bước bảo mật dữ liệu, người học phải NHÌN THẤY được lỗi đó. */
function makeFileCabinet(material, { openDrawer = null, labelMap = null } = {}) {
    const group = new THREE.Group();
    const width = 0.5, depth = 0.62, height = 1.35;
    group.add(box(material.cabinet, width, height, depth, 0, height / 2, 0));

    for (let i = 0; i < 4; i++) {
        const y = 0.2 + i * 0.31;
        const out = (openDrawer === i) ? 0.3 : 0.0;
        const front = box(material.cabinetFace, width - 0.04, 0.27, 0.03, 0, y, depth / 2 + 0.015 + out);
        group.add(front);
        group.add(box(material.metal, 0.16, 0.022, 0.03, 0, y, depth / 2 + 0.035 + out));
        if (out > 0) {
            // Ngăn kéo mở: thấy được gáy hồ sơ bên trong — đây chính là dữ liệu cá nhân đang phơi ra.
            group.add(box(material.panelLight, width - 0.08, 0.2, out * 2 - 0.04, 0, y + 0.02, depth / 2 + out / 2));
            for (let k = 0; k < 6; k++) {
                const folder = box(material.folder, 0.05, 0.17, out * 1.7,
                    -0.17 + k * 0.068, y + 0.05, depth / 2 + out / 2);
                group.add(folder);
            }
        }
    }
    if (labelMap) {
        const plate = new THREE.Mesh(
            new THREE.PlaneGeometry(0.34, 0.11),
            new THREE.MeshStandardMaterial({ map: labelMap, roughness: 0.8 })
        );
        plate.position.set(0, height - 0.12, depth / 2 + 0.02);
        group.add(plate);
    }
    return group;
}

/** Máy photocopy/in đa năng, có khay giấy ra ở giữa. */
function makePrinter(material, documentMap) {
    const group = new THREE.Group();
    group.add(box(material.cabinet, 0.78, 0.62, 0.66, 0, 0.31, 0));          // thân dưới
    group.add(box(material.panelLight, 0.78, 0.1, 0.66, 0, 0.67, 0));        // khay ra giấy
    group.add(box(material.cabinet, 0.78, 0.34, 0.66, 0, 0.89, 0));          // khối quét
    group.add(box(material.metalDark, 0.74, 0.03, 0.6, 0, 1.07, 0));         // nắp
    const panel = new THREE.Mesh(
        new THREE.PlaneGeometry(0.26, 0.14),
        new THREE.MeshBasicMaterial({ color: 0x1e3a8a })
    );
    panel.position.set(0.2, 1.075, 0.16);
    panel.rotation.x = -Math.PI / 2;
    group.add(panel);

    // Bản in BỎ QUÊN trên khay — chi tiết mang nội dung của bước "bảo mật dữ liệu ứng viên".
    const forgotten = makePaperStack(6, documentMap, { sheet: [0.21, 0.29] });
    forgotten.position.set(-0.02, 0.723, 0.06);
    group.add(forgotten);
    return group;
}

/** Vách kính ngăn phòng: khung nhôm + tấm kính trong. Kính KHÔNG được đưa vào danh sách vật cản của
 *  marker — nhìn xuyên qua được thì marker phía sau cũng phải sáng bình thường. */
function makeGlassPartition(material, { width, height = OFFICE.wallHeight, doorAt = null, doorWidth = 1.1,
                                        columnSpacing = 1.3 }) {
    const group = new THREE.Group();
    const segmentList = doorAt === null
        ? [[-width / 2, width / 2]]
        : [[-width / 2, doorAt - doorWidth / 2], [doorAt + doorWidth / 2, width / 2]];

    for (const [from, to] of segmentList) {
        const span = to - from;
        if (span <= 0.05) continue;
        const centerX = (from + to) / 2;
        const pane = box(material.glass, span, height - 0.16, 0.04, centerX, height / 2 + 0.02, 0);
        pane.castShadow = false;
        group.add(pane);
        // Khung: nẹp trên/dưới + đố đứng mỗi ~1.3m
        group.add(box(material.mullion, span, 0.07, 0.07, centerX, 0.035, 0));
        group.add(box(material.mullion, span, 0.07, 0.07, centerX, height - 0.035, 0));
        const columnCount = Math.max(1, Math.round(span / columnSpacing));
        for (let i = 0; i <= columnCount; i++) {
            group.add(box(material.mullion, 0.06, height, 0.07, from + (span * i) / columnCount, height / 2, 0));
        }
    }
    if (doorAt !== null) {
        // Khung cửa mở (không có cánh) — người học nhìn thấy lối vào phòng.
        group.add(box(material.mullion, doorWidth + 0.12, 0.08, 0.08, doorAt, height - 0.04, 0));
    }
    return group;
}

/** Chậu cây cảnh — thứ rẻ nhất để một văn phòng 3D trông "có người ở". */
function makePlant(material, scale = 1) {
    const group = new THREE.Group();
    group.add(tube(material.pot, 0.19, 0.34, 0, 0.17, 0, { hi: true }));
    group.add(tube(material.soil, 0.17, 0.04, 0, 0.35, 0, { hi: true }));
    const leafMaterial = new THREE.MeshStandardMaterial({ color: TONE.plant, roughness: 0.75, side: THREE.DoubleSide });
    const leafGeo = new THREE.SphereGeometry(0.16, 7, 5);
    for (let i = 0; i < 11; i++) {
        const angle = (i / 11) * Math.PI * 2 + Math.random();
        const radius = 0.1 + Math.random() * 0.22;
        const leaf = new THREE.Mesh(leafGeo, leafMaterial);
        leaf.position.set(Math.cos(angle) * radius, 0.55 + Math.random() * 0.5, Math.sin(angle) * radius);
        leaf.scale.set(1, 1.5 + Math.random(), 0.45);
        leaf.rotation.set(Math.random() * 0.5, angle, Math.random() * 0.4);
        leaf.castShadow = true;
        group.add(leaf);
    }
    group.scale.setScalar(scale);
    return group;
}

// ==================== NHÂN VẬT ====================
// Người được dựng theo lối tối giản (khối + viên nang + cầu) chứ không tải model có xương như cảnh công
// trường. Lý do: ở cảnh công trường, "công nhân KHÔNG đội mũ bảo hộ" là bằng chứng của một điểm nguy cơ
// nên hình người phải thật; ở đây nội dung nằm ở GIẤY TỜ và QUY TRÌNH, người chỉ cần đủ để biết khu vực
// nào đang có ai — dựng tối giản giúp cảnh nhẹ và giữ được phong cách đồ họa thống nhất.

const SKIN_LIST = [0xf0c8a0, 0xe0b088, 0xd9a077, 0xc98d62];
const SHIRT_LIST = [0x2563eb, 0x0f766e, 0x7c3aed, 0xb91c1c, 0x334155, 0x0891b2];

function makePerson({ seated = false, shirt = null, skin = null, rotY = 0, scale = 1 } = {}) {
    const group = new THREE.Group();
    const skinMaterial = new THREE.MeshStandardMaterial({
        color: skin ?? SKIN_LIST[(Math.random() * SKIN_LIST.length) | 0], roughness: 0.85,
    });
    const shirtMaterial = new THREE.MeshStandardMaterial({
        color: shirt ?? SHIRT_LIST[(Math.random() * SHIRT_LIST.length) | 0], roughness: 0.9,
    });
    const trouserMaterial = new THREE.MeshStandardMaterial({ color: 0x2b3444, roughness: 0.92 });

    const hipY = seated ? 0.47 : 0.86;
    const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.16, 0.36, 4, 10), shirtMaterial);
    torso.position.y = hipY + 0.3;
    torso.castShadow = true;
    group.add(torso);

    const head = new THREE.Mesh(new THREE.SphereGeometry(0.115, 14, 12), skinMaterial);
    head.position.y = hipY + 0.66;
    head.castShadow = true;
    group.add(head);

    // Tóc: một chỏm cầu cắt đôi, đủ để phân biệt mặt trước/sau khi nhìn từ trên xuống
    const hair = new THREE.Mesh(new THREE.SphereGeometry(0.12, 12, 8, 0, Math.PI * 2, 0, Math.PI * 0.62),
        new THREE.MeshStandardMaterial({ color: 0x2a2320, roughness: 0.95 }));
    hair.position.y = hipY + 0.675;
    group.add(hair);

    for (const sx of [-1, 1]) {
        const arm = new THREE.Mesh(new THREE.CapsuleGeometry(0.052, 0.34, 4, 8), shirtMaterial);
        arm.position.set(sx * 0.21, hipY + 0.31, seated ? 0.06 : 0);
        arm.rotation.z = sx * 0.14;
        arm.rotation.x = seated ? -0.55 : 0;
        arm.castShadow = true;
        group.add(arm);
    }

    if (seated) {
        for (const sx of [-1, 1]) {
            const thigh = new THREE.Mesh(new THREE.CapsuleGeometry(0.075, 0.3, 4, 8), trouserMaterial);
            thigh.position.set(sx * 0.1, hipY - 0.02, 0.2);
            thigh.rotation.x = Math.PI / 2;
            group.add(thigh);
            const shin = new THREE.Mesh(new THREE.CapsuleGeometry(0.065, 0.34, 4, 8), trouserMaterial);
            shin.position.set(sx * 0.1, hipY - 0.26, 0.36);
            group.add(shin);
        }
    } else {
        for (const sx of [-1, 1]) {
            const leg = new THREE.Mesh(new THREE.CapsuleGeometry(0.075, 0.6, 4, 8), trouserMaterial);
            leg.position.set(sx * 0.095, hipY - 0.42, 0);
            leg.castShadow = true;
            group.add(leg);
        }
    }

    group.rotation.y = rotY;
    group.scale.setScalar(scale);
    return group;
}

// ==================== TẠO THẾ GIỚI ====================

/**
 * Dựng toàn bộ văn phòng phòng Nhân sự vào `container` và trả về đối tượng điều khiển cho trang gọi.
 * Trả về CÙNG một hình dạng API với createSimWorld của cảnh công trường
 * ({ addMarker, focus, resetCamera, setRunning, dispose }) — pages/simulation.html dùng chung một mã
 * nghiệp vụ cho mọi bối cảnh, chỉ đổi hàm dựng cảnh theo SceneKey (xem js/sim-registry.js).
 *
 * @param {HTMLElement} container phần tử bọc canvas (phải có kích thước > 0)
 * @param {(text: string) => void} onProgress báo tiến độ để hiện lên màn hình chờ
 */
export async function createHrWorld(container, onProgress = () => {}) {
    onProgress('Đang dựng mặt bằng phòng Nhân sự...');

    // ---------- Renderer ----------
    const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.setSize(container.clientWidth, container.clientHeight);
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    // Khác hẳn cảnh ngoài trời (0.34): nguồn sáng ở đây là đèn trần và ánh sáng hắt qua cửa kính, dải
    // sáng hẹp hơn nhiều nên để exposure quanh 1 mới ra đúng độ sáng của một văn phòng có điều hòa.
    renderer.toneMappingExposure = 1.02;
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    container.insertBefore(renderer.domElement, container.firstChild);

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0xdfe7f0);

    const camera = new THREE.PerspectiveCamera(46, container.clientWidth / container.clientHeight, 0.1, 500);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.07;
    controls.minDistance = 6;
    controls.maxDistance = 58;
    // Chặn không cho hạ camera xuống dưới mặt sàn — mặt bằng không có trần nên góc nhìn hữu ích luôn
    // nằm ở nửa trên; thả tự do thì người học rất dễ "chui xuống gầm nhà" và không biết đường quay lên.
    controls.maxPolarAngle = Math.PI * 0.46;
    controls.target.set(0, 1, 0);

    // ---------- Ánh sáng dựa trên môi trường nội thất ----------
    // RoomEnvironment dựng sẵn một "hộp studio" có trần sáng/tường sáng rồi nướng thành env map. Đây là
    // thứ tạo ra phản chiếu mềm trên mặt bàn gỗ, khung nhôm và kính — không có nó, mọi bề mặt bóng đều
    // trả về màu đen và cảnh trông như đồ nhựa.
    onProgress('Đang tính ánh sáng nội thất...');
    const pmrem = new THREE.PMREMGenerator(renderer);
    const envTarget = pmrem.fromScene(new RoomEnvironment(), 0.04);
    scene.environment = envTarget.texture;
    pmrem.dispose();

    scene.add(new THREE.HemisphereLight(0xf3f7ff, 0x8d9099, 1.15));

    // "Mặt trời" hắt chéo qua vách kính phía +X: nguồn duy nhất đổ bóng, khung chiếu bó sát mặt bằng để
    // từng pixel shadow map dùng đúng chỗ, cho bóng ghế/bàn sắc nét thay vì răng cưa.
    const sun = new THREE.DirectionalLight(0xfff4e2, 1.9);
    sun.position.set(24, 20, 14);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    sun.shadow.camera.left = -22;
    sun.shadow.camera.right = 22;
    sun.shadow.camera.top = 20;
    sun.shadow.camera.bottom = -20;
    sun.shadow.camera.near = 1;
    sun.shadow.camera.far = 70;
    sun.shadow.bias = -0.0006;
    sun.shadow.normalBias = 0.02;
    scene.add(sun);

    // Đèn trần: vài nguồn điểm ấm rải đều cho các góc khuất không bị chết tối (không đổ bóng — đổ bóng
    // nhiều nguồn là thứ đắt nhất và ở đây không đem lại thông tin gì cho bài học).
    for (const [lx, lz] of [[-8, 6], [-8, -6], [6, -6], [7, 4], [0, 0]]) {
        const lamp = new THREE.PointLight(0xffeedd, 26, 16, 2);
        lamp.position.set(lx, 3.1, lz);
        scene.add(lamp);
    }

    // ---------- Vật liệu dùng chung ----------
    const carpet = carpetSurface();
    const M = {
        deskTop: new THREE.MeshStandardMaterial({ map: deskTopTexture(), roughness: 0.55, metalness: 0.02 }),
        wood: new THREE.MeshStandardMaterial({ color: TONE.wood, roughness: 0.62 }),
        woodDark: new THREE.MeshStandardMaterial({ color: TONE.woodDark, roughness: 0.68 }),
        metal: new THREE.MeshStandardMaterial({ color: TONE.metal, roughness: 0.34, metalness: 0.82 }),
        metalDark: new THREE.MeshStandardMaterial({ color: 0x4b5563, roughness: 0.42, metalness: 0.7 }),
        panelLight: new THREE.MeshStandardMaterial({ color: TONE.panel, roughness: 0.78 }),
        wall: new THREE.MeshStandardMaterial({ color: 0xf1f3f6, roughness: 0.94 }),
        wallAccent: new THREE.MeshStandardMaterial({ color: 0x25324a, roughness: 0.86 }),
        cabinet: new THREE.MeshStandardMaterial({ color: 0x6b7280, roughness: 0.5, metalness: 0.45 }),
        cabinetFace: new THREE.MeshStandardMaterial({ color: 0x7b8493, roughness: 0.46, metalness: 0.4 }),
        folder: new THREE.MeshStandardMaterial({ color: 0xd8b26a, roughness: 0.92 }),
        paper: new THREE.MeshStandardMaterial({ color: TONE.paper, roughness: 0.96 }),
        pot: new THREE.MeshStandardMaterial({ color: 0xc2603c, roughness: 0.85 }),
        soil: new THREE.MeshStandardMaterial({ color: 0x3b2f22, roughness: 1 }),
        mullion: new THREE.MeshStandardMaterial({ color: 0x8b95a1, roughness: 0.35, metalness: 0.85 }),
        // Kính: dùng độ trong suốt thường thay vì `transmission` — transmission bắt renderer dựng thêm một
        // lượt render hậu cảnh cho MỖI tấm kính, mà ở đây có hơn hai chục tấm.
        glass: new THREE.MeshPhysicalMaterial({
            color: 0xdff1ff, transparent: true, opacity: 0.19, roughness: 0.03,
            metalness: 0, reflectivity: 0.5, side: THREE.DoubleSide, depthWrite: false,
        }),
    };

    const occluderList = [];   // khối chắn tầm nhìn — dùng cho phép thử che khuất marker (js/sim-kit.js)
    const add = (obj, { occlude = false } = {}) => { scene.add(obj); if (occlude) occluderList.push(obj); return obj; };

    // ---------- Sàn ----------
    onProgress('Đang trải sàn và dựng vách...');
    const floorWidth = OFFICE.maxX - OFFICE.minX;
    const floorDepth = OFFICE.maxZ - OFFICE.minZ;
    const floor = new THREE.Mesh(
        new THREE.PlaneGeometry(floorWidth, floorDepth),
        new THREE.MeshStandardMaterial({ ...carpet, roughness: 1, metalness: 0 })
    );
    floor.rotation.x = -Math.PI / 2;
    floor.position.set((OFFICE.minX + OFFICE.maxX) / 2, 0, (OFFICE.minZ + OFFICE.maxZ) / 2);
    floor.receiveShadow = true;
    for (const map of [carpet.map, carpet.normalMap, carpet.roughnessMap]) {
        map.anisotropy = renderer.capabilities.getMaxAnisotropy();
    }
    scene.add(floor);

    /** Sàn gỗ lát riêng cho hai phòng họp — đổi vật liệu sàn là cách rẻ nhất để mắt đọc ra "đây là một
     *  phòng khác", trước cả khi nhìn thấy vách ngăn. */
    const addWoodFloor = (x, z, w, d) => {
        const patch = new THREE.Mesh(
            new THREE.PlaneGeometry(w, d),
            new THREE.MeshStandardMaterial({ map: woodFloorTexture([w / 3, d / 3]), roughness: 0.45 })
        );
        patch.rotation.x = -Math.PI / 2;
        patch.position.set(x, 0.006, z);
        patch.receiveShadow = true;
        scene.add(patch);
    };
    addWoodFloor(-8.5, -7.25, 11, 7.5);
    addWoodFloor(6.5, -7.25, 11, 7.5);

    // ---------- Vách kính bao quanh + thành phố bên ngoài ----------
    // Bốn mặt tường ngoài đều là kính khung nhôm (mặt bằng văn phòng hạng A điển hình ở Việt Nam), nên
    // nhìn từ mọi phía đều thấy được bên trong mà vẫn có cảm giác "đang ở trong một tòa nhà".
    const buildCurtainWall = (x, z, width, rotY) => {
        // Đố đứng cách 3m như hệ vách kính mặt dựng thật — để 1.3m như vách ngăn trong nhà thì nhìn từ
        // ngoài vào cả tòa nhà biến thành một cái lồng sắt.
        const wall = makeGlassPartition(M, { width, height: 3.2, columnSpacing: 3 });
        wall.position.set(x, 0, z);
        wall.rotation.y = rotY;
        scene.add(wall);
        // Bệ chân tường thấp — chặn khe hở giữa kính và sàn, đồng thời viền lại mặt bằng cho gọn
        const sill = box(M.panelLight, rotY === 0 ? width : 0.16, 0.32, rotY === 0 ? 0.16 : width, x, 0.16, z);
        scene.add(sill);
    };
    buildCurtainWall((OFFICE.minX + OFFICE.maxX) / 2, OFFICE.maxZ, floorWidth, 0);
    buildCurtainWall((OFFICE.minX + OFFICE.maxX) / 2, OFFICE.minZ, floorWidth, 0);
    buildCurtainWall(OFFICE.minX, (OFFICE.minZ + OFFICE.maxZ) / 2, floorDepth, Math.PI / 2);
    buildCurtainWall(OFFICE.maxX, (OFFICE.minZ + OFFICE.maxZ) / 2, floorDepth, Math.PI / 2);

    // Bán kính 90m, cao 76m: một vòng texture trải trên chu vi ~565m nên đặt repeat 1.5 thì mỗi tòa nhà
    // trong ảnh rộng khoảng 20m và cao khoảng 50m — đúng tỉ lệ so với mặt bằng 30×24m ở giữa. Để repeat 1
    // như ban đầu thì mỗi tòa nhà bị kéo rộng cả trăm mét và nền trời chỉ còn là vệt mờ.
    const skylineMap = skylineTexture();
    skylineMap.repeat.set(1.5, 1);
    const skyline = new THREE.Mesh(
        new THREE.CylinderGeometry(90, 90, 76, 48, 1, true),
        new THREE.MeshBasicMaterial({ map: skylineMap, side: THREE.BackSide, fog: false })
    );
    skyline.position.y = 35;
    scene.add(skyline);

    // Nền đất phải RỘNG HƠN vòng nhà cao tầng, nếu không nhìn thấy rìa đĩa cắt ngang chân các tòa nhà.
    const outsideGround = new THREE.Mesh(
        new THREE.CircleGeometry(260, 48),
        new THREE.MeshStandardMaterial({ color: 0xa9b6c6, roughness: 1 })
    );
    outsideGround.rotation.x = -Math.PI / 2;
    outsideGround.position.y = -0.06;
    scene.add(outsideGround);

    // ---------- Vách ngăn trong nhà ----------
    /** Tường đặc (thạch cao) — đưa vào danh sách vật cản vì đúng là chắn tầm nhìn thật. */
    const addWall = (x, z, width, rotY, { height = OFFICE.wallHeight, material = M.wall } = {}) => {
        const wall = texBox(material, width, height, 0.14, x, height / 2, z, rotY, 2.2);
        add(wall, { occlude: true });
        return wall;
    };

    // Vách giữa hai phòng họp (đặc, để hai cuộc họp không nhìn thấy nhau)
    addWall(-1.5, -7.25, 7.5, Math.PI / 2);
    // Mặt trước hai phòng họp: kính, có cửa mở — nhìn xuyên vào được nên KHÔNG tính là vật cản
    const interviewFront = makeGlassPartition(M, { width: 11, doorAt: 4.2 });
    interviewFront.position.set(-8.5, 0, OFFICE.roomSplitZ);
    scene.add(interviewFront);
    const meetingFront = makeGlassPartition(M, { width: 11, doorAt: -4.2 });
    meetingFront.position.set(6.5, 0, OFFICE.roomSplitZ);
    scene.add(meetingFront);

    // Bảng tên hai phòng, gắn trên đố kính cạnh cửa
    const addRoomPlate = (x, z, lineList, rotY = 0) => {
        const plate = new THREE.Mesh(
            new THREE.PlaneGeometry(1.5, 0.4),
            new THREE.MeshStandardMaterial({
                map: labelTexture(lineList, { accent: '#1d4ed8', size: [512, 136] }), roughness: 0.7,
            })
        );
        plate.position.set(x, 2.25, z);
        plate.rotation.y = rotY;
        scene.add(plate);
    };
    addRoomPlate(-6, OFFICE.roomSplitZ + 0.06, ['PHÒNG PHỎNG VẤN']);
    addRoomPlate(4.5, OFFICE.roomSplitZ + 0.06, ['PHÒNG HỌP TUYỂN DỤNG']);

    // ============================================================
    // KHU 1 — QUẦY LỄ TÂN & TIẾP NHẬN HỒ SƠ  (node 1)
    // ============================================================
    onProgress('Đang bố trí quầy lễ tân...');
    const cvMap = documentTexture({ heading: 'SƠ YẾU LÝ LỊCH', hasPhoto: true });
    const offerMap = documentTexture({ heading: 'THƯ MỜI NHẬN VIỆC', headingColor: '#047857' });
    const contractMap = documentTexture({ heading: 'HỢP ĐỒNG THỬ VIỆC', stampText: 'CHƯA KÝ' });

    {
        const { x, z } = OFFICE.reception;
        // Quầy chữ L: thân quầy + mặt bàn nhô ra, cao 1.1m như quầy lễ tân thật
        const counterFront = texBox(M.woodDark, 4.4, 1.06, 0.7, x, 0.53, z, 0, 1.4);
        add(counterFront, { occlude: true });
        add(texBox(M.deskTop, 4.7, 0.07, 0.9, x, 1.1, z, 0, 1.4));
        const counterSide = texBox(M.woodDark, 0.7, 1.06, 2.0, x + 2.2, 0.53, z - 1.0, 0, 1.4);
        add(counterSide, { occlude: true });
        add(texBox(M.deskTop, 0.9, 0.07, 2.1, x + 2.2, 1.1, z - 1.0, 0, 1.4));

        // Tường logo phía sau quầy
        const logoWall = addWall(x, z - 2.6, 6.4, 0, { height: 1.8, material: M.wallAccent });
        const logo = new THREE.Mesh(
            new THREE.PlaneGeometry(4.2, 0.95),
            new THREE.MeshStandardMaterial({
                map: labelTexture(['PHÒNG NHÂN SỰ', '#HÒA BÌNH CONSTRUCTIONS GROUP'],
                    { bg: '#25324a', fg: '#ffffff', accent: '#3b82f6', size: [640, 168] }),
                roughness: 0.8,
            })
        );
        logo.position.set(x, 1.12, z - 2.52);
        scene.add(logo);
        void logoWall;

        // Chuyên viên tiếp nhận ngồi trong quầy, ứng viên đứng nộp hồ sơ bên ngoài
        const staff = makePerson({ seated: true, shirt: 0x1d4ed8, rotY: Math.PI });
        staff.position.set(x - 0.4, 0, z - 1.15);
        scene.add(staff);
        const staffChair = makeOfficeChair(M, { rotY: Math.PI });
        staffChair.position.set(x - 0.4, 0, z - 1.35);
        scene.add(staffChair);

        const candidate = makePerson({ shirt: 0x0f766e, rotY: 0 });
        candidate.position.set(x + 0.3, 0, z + 0.85);
        scene.add(candidate);
        scene.add(contactShadow(1.3, x + 0.3, z + 0.85, 0.4));

        // Chồng hồ sơ CHƯA NHẬP HỆ THỐNG trên quầy — đúng chi tiết mà tình huống bước 1 nói tới
        for (let i = 0; i < 3; i++) {
            const stack = makePaperStack(7, cvMap);
            stack.position.set(x - 1.5 + i * 0.55, 1.14, z - 0.1);
            scene.add(stack);
        }
        const tray = makeDocumentTray(M, 2, cvMap);
        tray.position.set(x + 1.5, 1.14, z - 0.12);
        scene.add(tray);

        // Khu ghế chờ ứng viên
        const sofaX = x - 4.4, sofaZ = z + 2.6;
        add(box(M.panelLight, 2.4, 0.42, 0.86, sofaX, 0.21, sofaZ));
        add(box(M.panelLight, 2.4, 0.5, 0.2, sofaX, 0.55, sofaZ - 0.33));
        add(box(M.deskTop, 1.1, 0.06, 0.6, sofaX, 0.42, sofaZ + 1.15));
        for (const sx of [-1, 1]) {
            add(tube(M.metalDark, 0.02, 0.4, sofaX + sx * 0.45, 0.2, sofaZ + 1.15));
        }
        const waitingA = makePerson({ seated: true, shirt: 0xb91c1c, rotY: 0.15 });
        waitingA.position.set(sofaX - 0.6, 0, sofaZ - 0.05);
        scene.add(waitingA);
        const waitingB = makePerson({ seated: true, shirt: 0x7c3aed, rotY: -0.1 });
        waitingB.position.set(sofaX + 0.62, 0, sofaZ - 0.05);
        scene.add(waitingB);

        // Standee tuyển dụng
        const standee = new THREE.Mesh(
            new THREE.PlaneGeometry(0.8, 1.8),
            new THREE.MeshStandardMaterial({
                map: labelTexture(['TUYỂN DỤNG', '#Kỹ sư giám sát', '#Nộp hồ sơ tại quầy'],
                    { bg: '#1d4ed8', fg: '#ffffff', accent: '#f59e0b', size: [320, 720] }),
                roughness: 0.85, side: THREE.DoubleSide,
            })
        );
        standee.position.set(x + 3.6, 1.0, z + 1.6);
        standee.rotation.y = -0.5;
        scene.add(standee);
        scene.add(makePlant(M, 1.1).translateX(x - 6.6).translateZ(z - 0.4));
    }

    // ============================================================
    // KHU 2 — BÀN SÀNG LỌC HỒ SƠ  (node 2)
    // ============================================================
    {
        const { x, z } = OFFICE.screening;
        const screeningScreen = screenTexture('SÀNG LỌC HỒ SƠ ỨNG VIÊN');
        for (let i = 0; i < 2; i++) {
            const desk = makeDesk(M, { width: 1.8, depth: 0.85 });
            desk.position.set(x - 1.05 + i * 2.1, 0, z);
            scene.add(desk);

            const monitor = makeMonitor(M, screeningScreen, { rotY: Math.PI + (i ? -0.2 : 0.2) });
            monitor.position.set(x - 1.05 + i * 2.1, 0.79, z - 0.18);
            scene.add(monitor);

            const chair = makeOfficeChair(M, { rotY: Math.PI });
            chair.position.set(x - 1.05 + i * 2.1, 0, z + 0.75);
            scene.add(chair);
        }
        // Chuyên viên đang ngồi sàng lọc — chính là "bạn" trong tình huống
        const recruiter = makePerson({ seated: true, shirt: 0x0891b2, rotY: Math.PI });
        recruiter.position.set(x - 1.05, 0, z + 0.72);
        scene.add(recruiter);

        // 40 bộ hồ sơ đang chờ sàng lọc: xếp thành các chồng cao thấp khác nhau
        for (let i = 0; i < 5; i++) {
            const stack = makePaperStack(9 + (i % 3) * 6, cvMap);
            stack.position.set(x - 1.7 + i * 0.42, 0.785, z - 0.02 + (i % 2) * 0.22);
            scene.add(stack);
        }
        const tray = makeDocumentTray(M, 3, cvMap);
        tray.position.set(x + 1.75, 0.785, z - 0.2);
        scene.add(tray);
        scene.add(contactShadow(3.6, x, z, 0.3, 0.6));

        // Vách ngăn bàn thấp — nhận diện đây là "pod làm việc" chứ không phải bàn kê rời
        add(box(M.panelLight, 4.3, 0.5, 0.06, x, 0.99, z - 0.46));
    }

    // ============================================================
    // KHU 3 — MÁY IN & TỦ HỒ SƠ (bảo mật dữ liệu)  (node 3)
    // ============================================================
    {
        const { x, z } = OFFICE.printer;
        const printer = makePrinter(M, cvMap);
        printer.position.set(x - 0.6, 0, z - 1.6);
        add(printer, { occlude: true });
        scene.add(contactShadow(1.6, x - 0.6, z - 1.6, 0.45));

        // Ba tủ hồ sơ dựa tường Đông; tủ giữa CÒN MỞ NGĂN KÉO — lỗi mà người học phải nhìn ra
        const labelList = [
            labelTexture(['HỒ SƠ ỨNG VIÊN'], { size: [340, 110], accent: '#dc2626' }),
            labelTexture(['HỒ SƠ NHÂN VIÊN'], { size: [340, 110], accent: '#dc2626' }),
            labelTexture(['HỢP ĐỒNG LAO ĐỘNG'], { size: [340, 110], accent: '#dc2626' }),
        ];
        for (let i = 0; i < 3; i++) {
            const cabinet = makeFileCabinet(M, { openDrawer: i === 1 ? 1 : null, labelMap: labelList[i] });
            cabinet.position.set(x + 1.5, 0, z - 0.9 + i * 0.62);
            cabinet.rotation.y = -Math.PI / 2;
            add(cabinet, { occlude: true });
        }
        // Bàn nhỏ cạnh máy in, có tập bản in bỏ quên
        const sideTable = makeDesk(M, { width: 1.2, depth: 0.6, drawer: false });
        sideTable.position.set(x - 0.6, 0, z + 0.6);
        scene.add(sideTable);
        const forgotten = makePaperStack(5, cvMap);
        forgotten.position.set(x - 0.6, 0.775, z + 0.6);
        scene.add(forgotten);
        scene.add(makePlant(M, 0.95).translateX(x - 1.9).translateZ(z + 2.3));
    }

    // ============================================================
    // KHU 4 — PHÒNG PHỎNG VẤN  (node 4)
    // ============================================================
    onProgress('Đang bố trí phòng phỏng vấn...');
    {
        const { x, z } = OFFICE.interview;
        // Bàn phỏng vấn dài: hội đồng ngồi phía -Z, ứng viên ngồi một mình phía +Z
        add(texBox(M.deskTop, 2.8, 0.07, 1.15, x, 0.75, z, 0, 1.3));
        for (const sx of [-1, 1]) {
            add(box(M.woodDark, 0.1, 0.72, 0.9, x + sx * 1.25, 0.36, z));
        }

        const panelShirtList = [0x334155, 0x1d4ed8, 0x7c3aed];
        for (let i = 0; i < 3; i++) {
            const px = x - 0.9 + i * 0.9;
            const chair = makeMeetingChair(M, { rotY: 0 });
            chair.position.set(px, 0, z - 1.0);
            scene.add(chair);
            const person = makePerson({ seated: true, shirt: panelShirtList[i], rotY: 0 });
            person.position.set(px, 0, z - 1.05);
            scene.add(person);
            // Mỗi thành viên hội đồng một bản CV — nhưng KHÔNG có phiếu đánh giá theo tiêu chí, đúng
            // như tình huống mô tả
            const cv = makePaperStack(2, cvMap);
            cv.position.set(px, 0.79, z - 0.32);
            scene.add(cv);
        }

        const candidateChair = makeMeetingChair(M, { rotY: Math.PI });
        candidateChair.position.set(x, 0, z + 1.05);
        scene.add(candidateChair);
        const candidate = makePerson({ seated: true, shirt: 0x0f766e, rotY: Math.PI });
        candidate.position.set(x, 0, z + 1.1);
        scene.add(candidate);
        scene.add(contactShadow(4.4, x, z, 0.3, 0.7));

        // Màn hình trình chiếu trên tường Tây
        const tv = box(M.metalDark, 0.08, 1.0, 1.8, OFFICE.minX + 0.5, 1.6, z);
        add(tv);
        const tvScreen = new THREE.Mesh(
            new THREE.PlaneGeometry(1.7, 0.92),
            new THREE.MeshBasicMaterial({ map: screenTexture('PHIẾU ĐÁNH GIÁ ỨNG VIÊN') })
        );
        tvScreen.position.set(OFFICE.minX + 0.55, 1.6, z);
        tvScreen.rotation.y = Math.PI / 2;
        scene.add(tvScreen);
        scene.add(makePlant(M, 1.15).translateX(x + 4.2).translateZ(z - 2.6));
    }

    // ============================================================
    // KHU 5 — PHÒNG HỌP ĐÁNH GIÁ & THƯ MỜI NHẬN VIỆC  (node 5)
    // ============================================================
    {
        const { x, z } = OFFICE.meeting;
        // Bàn họp tròn
        const table = tube(M.deskTop, 1.15, 0.07, x, 0.75, z, { hi: true });
        add(table);
        add(tube(M.metalDark, 0.09, 0.72, x, 0.36, z, { hi: true }));
        add(tube(M.metalDark, 0.42, 0.04, x, 0.02, z, { hi: true }));

        const seatShirtList = [0x1d4ed8, 0xb91c1c, 0x0891b2, 0x334155];
        for (let i = 0; i < 4; i++) {
            const angle = (i / 4) * Math.PI * 2 + Math.PI / 4;
            const cx = x + Math.cos(angle) * 1.6;
            const cz = z + Math.sin(angle) * 1.6;
            const facing = Math.atan2(x - cx, z - cz);
            const chair = makeMeetingChair(M, { rotY: facing });
            chair.position.set(cx, 0, cz);
            scene.add(chair);
            if (i < 3) {
                const person = makePerson({ seated: true, shirt: seatShirtList[i], rotY: facing });
                person.position.set(cx, 0, cz);
                scene.add(person);
            }
        }
        // Thư mời nhận việc đặt giữa bàn — đây là "sản phẩm" của bước này
        const offer = makePaperStack(3, offerMap, { sheet: [0.24, 0.33] });
        offer.position.set(x, 0.79, z);
        scene.add(offer);
        scene.add(contactShadow(4.2, x, z, 0.3));

        const screen = new THREE.Mesh(
            new THREE.PlaneGeometry(2.2, 1.24),
            new THREE.MeshBasicMaterial({ map: screenTexture('KẾT QUẢ PHỎNG VẤN') })
        );
        screen.position.set(x, 1.72, OFFICE.minZ + 0.2);
        scene.add(screen);
        add(box(M.metalDark, 2.34, 1.38, 0.07, x, 1.72, OFFICE.minZ + 0.15));
        scene.add(makePlant(M, 1.05).translateX(x + 3.6).translateZ(z + 2.2));
    }

    // ============================================================
    // KHU 6 — BÀN LẬP HỒ SƠ NHÂN VIÊN MỚI  (node 6)
    // ============================================================
    {
        const { x, z } = OFFICE.newFile;
        const desk = makeDesk(M, { width: 2.0, depth: 0.9 });
        desk.position.set(x, 0, z);
        scene.add(desk);
        const chair = makeOfficeChair(M, { rotY: Math.PI });
        chair.position.set(x - 0.4, 0, z + 0.8);
        scene.add(chair);
        const hrStaff = makePerson({ seated: true, shirt: 0x7c3aed, rotY: Math.PI });
        hrStaff.position.set(x - 0.4, 0, z + 0.78);
        scene.add(hrStaff);

        const monitor = makeMonitor(M, screenTexture('HỒ SƠ NHÂN VIÊN MỚI'), { rotY: Math.PI });
        monitor.position.set(x - 0.4, 0.79, z - 0.24);
        scene.add(monitor);

        // Bìa hồ sơ đang mở + BẢN CHÍNH bằng cấp/CCCD đặt riêng: chi tiết trung tâm của tình huống
        // "giữ bản chính giấy tờ của người lao động".
        const folderBase = box(M.folder, 0.46, 0.02, 0.34, x + 0.75, 0.78, z + 0.02);
        scene.add(folderBase);
        const originals = makePaperStack(4, documentTexture({ heading: 'BẢN CHÍNH', headingColor: '#b91c1c' }),
            { sheet: [0.19, 0.26] });
        originals.position.set(x + 0.75, 0.795, z + 0.02);
        scene.add(originals);
        const checklist = makePaperStack(2, documentTexture({ heading: 'CHECKLIST HỒ SƠ' }), { sheet: [0.2, 0.28] });
        checklist.position.set(x + 0.28, 0.785, z + 0.18);
        scene.add(checklist);

        // Nhân viên mới đứng chờ bên cạnh bàn
        const newHire = makePerson({ shirt: 0x16a34a, rotY: -Math.PI / 2 });
        newHire.position.set(x + 1.55, 0, z - 0.2);
        scene.add(newHire);
        scene.add(contactShadow(1.2, x + 1.55, z - 0.2, 0.4));
        scene.add(contactShadow(3.4, x, z, 0.28, 0.65));
    }

    // ============================================================
    // KHU 7 — BÀN HỢP ĐỒNG THỬ VIỆC & KHAI BÁO BHXH  (node 7)
    // ============================================================
    {
        const { x, z } = OFFICE.contract;
        const desk = makeDesk(M, { width: 1.5, depth: 0.8, drawer: false });
        desk.position.set(x, 0, z);
        desk.rotation.y = -Math.PI / 2;
        scene.add(desk);

        const contracts = makePaperStack(6, contractMap, { sheet: [0.23, 0.32] });
        contracts.position.set(x, 0.775, z + 0.1);
        scene.add(contracts);
        const pen = tube(M.metalDark, 0.008, 0.14, x - 0.22, 0.782, z - 0.14, { axis: 'x' });
        scene.add(pen);

        const chair = makeOfficeChair(M, { rotY: Math.PI / 2 });
        chair.position.set(x - 0.9, 0, z);
        scene.add(chair);

        // Tủ tài liệu bảo hiểm xã hội dựa tường
        const bhxhCabinet = makeFileCabinet(M, {
            labelMap: labelTexture(['BHXH – THUẾ TNCN'], { size: [340, 110], accent: '#047857' }),
        });
        bhxhCabinet.position.set(x + 1.6, 0, z - 1.5);
        bhxhCabinet.rotation.y = -Math.PI / 2;
        add(bhxhCabinet, { occlude: true });

        const noticeBoard = new THREE.Mesh(
            new THREE.PlaneGeometry(1.2, 0.85),
            new THREE.MeshStandardMaterial({
                map: labelTexture(['BÁO TĂNG BHXH', '#Trong 30 ngày kể từ', '#ngày giao kết hợp đồng'],
                    { accent: '#047857', size: [480, 340] }),
                roughness: 0.9,
            })
        );
        noticeBoard.position.set(OFFICE.maxX - 0.22, 1.7, z + 0.3);
        noticeBoard.rotation.y = -Math.PI / 2;
        scene.add(noticeBoard);
        scene.add(contactShadow(2.6, x, z, 0.28));
    }

    // ============================================================
    // KHU 8 — CHỖ NGỒI NHÂN VIÊN MỚI / ONBOARDING  (node 8)
    // ============================================================
    {
        const { x, z } = OFFICE.onboard;
        const desk = makeDesk(M, { width: 1.8, depth: 0.85 });
        desk.position.set(x, 0, z);
        scene.add(desk);
        const chair = makeOfficeChair(M, { rotY: -0.5, accent: 0x475569 });
        chair.position.set(x + 0.15, 0, z + 0.85);
        scene.add(chair);

        // Bàn TRỐNG: thiết bị vẫn còn trong thùng carton chưa mở, chưa có màn hình, chưa có tài khoản —
        // hình ảnh trực tiếp của lỗi "chưa chuẩn bị gì cho ngày đầu tiên".
        add(box(new THREE.MeshStandardMaterial({ color: 0xc8a06a, roughness: 0.96 }), 0.5, 0.34, 0.42, x - 0.45, 0.91, z - 0.05));
        add(box(M.metalDark, 0.5, 0.012, 0.05, x - 0.45, 1.085, z - 0.05));
        const badge = new THREE.Mesh(
            new THREE.PlaneGeometry(0.16, 0.24),
            new THREE.MeshStandardMaterial({
                map: labelTexture(['NHÂN VIÊN MỚI', '#Chưa cấp tài khoản'], { accent: '#f59e0b', size: [200, 300] }),
                roughness: 0.85, side: THREE.DoubleSide,
            })
        );
        badge.rotation.x = -Math.PI / 2;
        badge.position.set(x + 0.55, 0.783, z + 0.05);
        scene.add(badge);

        const newcomer = makePerson({ shirt: 0x16a34a, rotY: -0.3 });
        newcomer.position.set(x + 1.35, 0, z + 0.5);
        scene.add(newcomer);
        scene.add(contactShadow(1.2, x + 1.35, z + 0.5, 0.4));
        scene.add(contactShadow(3.2, x, z, 0.28, 0.7));

        // Bảng thông báo nội quy treo cạnh khu vực — chưa ai phổ biến cho người mới
        const rules = new THREE.Mesh(
            new THREE.PlaneGeometry(1.3, 0.9),
            new THREE.MeshStandardMaterial({
                map: labelTexture(['NỘI QUY LAO ĐỘNG', '#& AN TOÀN VỆ SINH LAO ĐỘNG'],
                    { accent: '#dc2626', size: [520, 360] }),
                roughness: 0.9,
            })
        );
        rules.position.set(x + 3.2, 1.7, z - 1.4);
        rules.rotation.y = -0.35;
        scene.add(rules);
    }

    // ---------- Hành lang: cây xanh + cây nước ----------
    scene.add(makePlant(M, 1.25).translateX(-1.2).translateZ(-1.6));
    scene.add(makePlant(M, 0.9).translateX(13.4).translateZ(-2.2));
    scene.add(makePlant(M, 1.0).translateX(-13.6).translateZ(9.6));
    {
        const cooler = new THREE.Group();
        cooler.add(box(M.panelLight, 0.36, 1.0, 0.36, 0, 0.5, 0));
        cooler.add(tube(new THREE.MeshPhysicalMaterial({
            color: 0x9ad4f5, transparent: true, opacity: 0.55, roughness: 0.15,
        }), 0.16, 0.42, 0, 1.2, 0, { hi: true }));
        cooler.position.set(1.6, 0, -1.7);
        add(cooler);
    }

    // ==================== VÒNG LẶP RENDER + API CHO TRANG ====================
    const clock = new THREE.Clock();
    let running = true;
    let rafId = 0;

    const director = createDirector({
        container, camera, controls, occluderList,
        centerX: 0, centerZ: 0,
        focusDistance: 9.5,     // không gian trong nhà chật hơn công trường nhiều, lùi xa là chui ra ngoài tường
        markerScaleBase: 16,
    });

    function render() {
        rafId = requestAnimationFrame(render);
        const delta = Math.min(clock.getDelta(), 0.1);

        director.updateTween(delta);
        controls.update();
        renderer.render(scene, camera);
        director.updateMarkers();
    }

    // Bay vào cảnh: mở bằng một cú lia từ trên cao rồi hạ về góc nhìn làm việc. Hai giây này vừa che thời
    // gian biên dịch shader của khung hình đầu tiên (luôn giật), vừa cho người học thấy toàn bộ mặt bằng
    // trước khi bắt đầu đi theo quy trình. Góc mặc định nhìn từ phía cửa vào (+Z) hướng vào trong: thấy
    // được lễ tân ở gần, các bàn nghiệp vụ ở giữa và hai phòng họp phía trong cùng.
    const HOME_POSITION = new THREE.Vector3(3, 17, 27);
    const HOME_TARGET = new THREE.Vector3(0, 1.2, -1);
    camera.position.set(10, 30, 42);
    controls.target.set(0, 2, 2);
    director.setHome(HOME_POSITION, HOME_TARGET);
    render();
    director.tweenCamera(HOME_POSITION, HOME_TARGET, 2.2);

    const resizeObserver = new ResizeObserver(() => {
        const w = container.clientWidth, h = container.clientHeight;
        if (!w || !h) return;
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
        renderer.setSize(w, h);
    });
    resizeObserver.observe(container);

    onProgress('Đã sẵn sàng.');

    return {
        scene, camera, renderer, controls,

        // Tự xoay / độ sáng / chụp khung hình — xem js/sim-kit.js. 1.02 là độ phơi sáng gốc của cảnh này.
        ...createViewerControl({ renderer, controls, scene, camera, baseExposure: 1.02 }),

        addMarker: director.addMarker,
        focus: director.focus,
        resetCamera: director.resetCamera,

        setRunning(value) {
            if (value === running) return;
            running = value;
            if (value) { clock.getDelta(); render(); }
            else cancelAnimationFrame(rafId);
        },

        dispose() {
            cancelAnimationFrame(rafId);
            resizeObserver.disconnect();
            controls.dispose();
            renderer.dispose();
            envTarget.dispose();
            renderer.domElement.remove();
        },
    };
}
