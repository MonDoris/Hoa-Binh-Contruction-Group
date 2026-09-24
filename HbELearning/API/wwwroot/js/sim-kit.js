// File: wwwroot/js/sim-kit.js
// ============================================================================================
// BỘ DỰNG CẢNH DÙNG CHUNG CHO MỌI BỐI CẢNH MÔ PHỎNG 3D
//
// Mỗi bối cảnh (công trường xây dựng, văn phòng nhân sự, ...) là một file riêng dựng đúng không gian
// của mình, nhưng tất cả đều cần chung ba nhóm việc dưới đây — tách ra đây để không phải chép lại mỗi
// lần thêm một bối cảnh mới:
//
//   1. TEXTURE SINH BẰNG THUẬT TOÁN — nhiễu lặp được, fBm, đổi trường độ cao thành normal map, và
//      makeSurface() sinh bộ 3 texture (màu + normal + độ nhám) khớp nhau từ cùng một trường độ cao.
//      Không tải một byte ảnh nào từ mạng, và normal map luôn ăn khớp tuyệt đối với ảnh màu.
//   2. TIỆN ÍCH DỰNG HÌNH — box/texBox/tube/contactShadow, dùng chung geometry đơn vị để tiết kiệm
//      bộ nhớ GPU khi cảnh có hàng trăm khối nhỏ.
//   3. ĐẠO DIỄN CAMERA & ĐIỂM ĐÁNH DẤU (createDirector) — chiếu tọa độ 3D của từng điểm tình huống ra
//      pixel của marker DOM mỗi khung hình, và lia camera mượt tới điểm mà người học vừa bấm.
//
// Là ES module vì Three.js không còn phát hành bản UMD/global — ngoại lệ duy nhất của dự án, khớp với
// importmap khai báo trong pages/simulation.html.
// ============================================================================================

import * as THREE from 'three';

// ==================== TEXTURE SINH BẰNG THUẬT TOÁN ====================
// Toàn bộ vân bề mặt trong cảnh được sinh tại chỗ bằng JS thay vì tải ảnh — giữ trang nhẹ (không thêm
// một byte tải về nào) và cho phép sinh kèm NORMAL MAP khớp hoàn hảo với ảnh màu, thứ gần như không bao
// giờ có khi đi nhặt ảnh texture rời trên mạng.

/** Nhiễu giá trị (value noise) LẶP ĐƯỢC: ô lưới `cells` × `cells` giá trị ngẫu nhiên rồi nội suy mượt
 *  (smoothstep) lên `size` × `size`. Chỉ số ô được lấy dư theo `cells` nên mép trái nối liền mép phải —
 *  đây là điều kiện bắt buộc để texture lặp 30 lần trên mặt đất mà không lộ đường kẻ ô vuông. */
export function tileableNoise(size, cells) {
    const grid = new Float32Array(cells * cells);
    for (let i = 0; i < grid.length; i++) grid[i] = Math.random();

    const out = new Float32Array(size * size);
    const smooth = (t) => t * t * (3 - 2 * t);
    for (let y = 0; y < size; y++) {
        const fy = (y / size) * cells, gy = Math.floor(fy), ty = smooth(fy - gy);
        const y0 = ((gy % cells) + cells) % cells, y1 = (y0 + 1) % cells;
        for (let x = 0; x < size; x++) {
            const fx = (x / size) * cells, gx = Math.floor(fx), tx = smooth(fx - gx);
            const x0 = ((gx % cells) + cells) % cells, x1 = (x0 + 1) % cells;
            const a = grid[y0 * cells + x0], b = grid[y0 * cells + x1];
            const c = grid[y1 * cells + x0], d = grid[y1 * cells + x1];
            out[y * size + x] = (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }
    }
    return out;
}

/** Chồng nhiều lớp nhiễu tần số tăng dần / biên độ giảm dần (fBm). Một lớp nhiễu đơn trông như sương mù;
 *  chồng 4-5 lớp mới ra được cấu trúc "có mảng lớn, có hạt nhỏ" giống vật liệu thật. */
export function fbm(size, octaveList) {
    const acc = new Float32Array(size * size);
    let total = 0;
    for (const [cells, amp] of octaveList) {
        const layer = tileableNoise(size, cells);
        for (let i = 0; i < acc.length; i++) acc[i] += layer[i] * amp;
        total += amp;
    }
    for (let i = 0; i < acc.length; i++) acc[i] /= total;
    return acc;
}

export function makeDataTexture(data, size, { srgb = false, repeat = 1 } = {}) {
    const tex = new THREE.DataTexture(data, size, size, THREE.RGBAFormat);
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    tex.repeat.set(repeat, repeat);
    tex.generateMipmaps = true;
    tex.minFilter = THREE.LinearMipmapLinearFilter;
    tex.magFilter = THREE.LinearFilter;
    if (srgb) tex.colorSpace = THREE.SRGBColorSpace;
    tex.needsUpdate = true;
    return tex;
}

/** Đổi trường độ cao (height field) thành normal map bằng sai phân trung tâm (Sobel rút gọn), lấy chỉ số
 *  vòng quanh nên normal map cũng lặp khít như ảnh màu sinh từ cùng trường độ cao đó. */
export function heightToNormalTexture(height, size, strength, repeat) {
    const data = new Uint8Array(size * size * 4);
    const at = (x, y) => height[(((y % size) + size) % size) * size + (((x % size) + size) % size)];
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const dx = (at(x + 1, y) - at(x - 1, y)) * strength;
            const dy = (at(x, y + 1) - at(x, y - 1)) * strength;
            const len = Math.hypot(dx, dy, 1);
            const i = (y * size + x) * 4;
            data[i] = (-dx / len * 0.5 + 0.5) * 255;
            data[i + 1] = (-dy / len * 0.5 + 0.5) * 255;
            data[i + 2] = (1 / len * 0.5 + 0.5) * 255;
            data[i + 3] = 255;
        }
    }
    return makeDataTexture(data, size, { repeat });
}

export function mixColor(a, b, t) {
    return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
}

/** Bộ 3 texture (màu + normal + độ nhám) sinh từ CÙNG một trường độ cao — chi tiết nổi, màu và độ bóng
 *  vì thế luôn ăn khớp nhau: chỗ lõm thì tối hơn và nhám hơn, đúng như bề mặt thật bị bám bụi. */
export function makeSurface({ size = 256, octaveList, darkColor, lightColor, repeat = 1, normalStrength = 30,
                       roughLow = 0.75, roughHigh = 1.0, speckle = 0 }) {
    const height = fbm(size, octaveList);
    const color = new Uint8Array(size * size * 4);
    const rough = new Uint8Array(size * size * 4);
    for (let i = 0; i < size * size; i++) {
        let h = height[i];
        // Đốm sỏi/vụn: ngưỡng trên nhiễu tần số cao, đủ để mắt đọc ra "mặt đất có hạt" khi nhìn gần.
        if (speckle > 0 && Math.random() < speckle) h = Math.min(1, h * 0.55);
        const c = mixColor(darkColor, lightColor, h);
        color[i * 4] = c[0]; color[i * 4 + 1] = c[1]; color[i * 4 + 2] = c[2]; color[i * 4 + 3] = 255;
        const r = (roughLow + (roughHigh - roughLow) * (1 - h)) * 255;
        rough[i * 4] = 0; rough[i * 4 + 1] = r; rough[i * 4 + 2] = 0; rough[i * 4 + 3] = 255;
    }
    return {
        map: makeDataTexture(color, size, { srgb: true, repeat }),
        normalMap: heightToNormalTexture(height, size, normalStrength, repeat),
        roughnessMap: makeDataTexture(rough, size, { repeat }),
    };
}

/** Texture vẽ tay trên <canvas> — dùng cho những thứ có HÌNH VẼ rõ ràng (gạch xây, tôn sóng, biển báo
 *  chữ tiếng Việt) mà nhiễu ngẫu nhiên không tạo ra được. */
export function canvasTexture(width, height, draw, { srgb = true, repeat = [1, 1] } = {}) {
    const canvas = document.createElement('canvas');
    canvas.width = width; canvas.height = height;
    draw(canvas.getContext('2d'), width, height);
    const tex = new THREE.CanvasTexture(canvas);
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    tex.repeat.set(repeat[0], repeat[1]);
    if (srgb) tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 8;
    return tex;
}

// ==================== TIỆN ÍCH DỰNG HÌNH ====================
const BOX = new THREE.BoxGeometry(1, 1, 1);
const CYL = new THREE.CylinderGeometry(0.5, 0.5, 1, 12);
const CYL_HI = new THREE.CylinderGeometry(0.5, 0.5, 1, 20);

export function box(mat, sx, sy, sz, x, y, z, rotY = 0) {
    const mesh = new THREE.Mesh(BOX, mat);
    mesh.scale.set(sx, sy, sz);
    mesh.position.set(x, y, z);
    mesh.rotation.y = rotY;
    mesh.castShadow = mesh.receiveShadow = true;
    return mesh;
}

/** Hộp có VÂN TÍNH THEO MÉT THẬT thay vì theo mặt hộp.
 *
 *  BoxGeometry trải UV 0→1 cho mọi mặt bất kể mặt đó to hay nhỏ, nên nếu chỉ scale một khối lập phương
 *  đơn vị thì cùng một vật liệu bê tông sẽ bị kéo giãn thành vệt dọc trên cột 0,4×3m và bị nén li ti trên
 *  sàn 9×6m. Hàm này nhân UV từng mặt với kích thước thật của mặt đó chia cho `tile` (cỡ một ô vân, tính
 *  bằng mét) — kết quả là viên gạch trên tường 2m và trên tường 9m to bằng nhau, đúng như ngoài đời.
 *  Đổi lại phải tạo geometry riêng cho mỗi khối, nhưng số khối có vân trong cảnh chỉ vài chục nên không
 *  đáng kể so với việc nhìn thấy vân sai tỉ lệ ở mọi bề mặt lớn nhất của cảnh. */
export function texBox(mat, sx, sy, sz, x, y, z, rotY = 0, tile = 1.5) {
    const geo = new THREE.BoxGeometry(sx, sy, sz);
    const uv = geo.attributes.uv;
    // Thứ tự mặt của BoxGeometry: +X, -X, +Y, -Y, +Z, -Z; mỗi mặt 4 đỉnh (1 phân đoạn).
    const faceSpanList = [[sz, sy], [sz, sy], [sx, sz], [sx, sz], [sx, sy], [sx, sy]];
    faceSpanList.forEach(([spanU, spanV], face) => {
        for (let i = 0; i < 4; i++) {
            const k = face * 4 + i;
            uv.setXY(k, uv.getX(k) * spanU / tile, uv.getY(k) * spanV / tile);
        }
    });
    uv.needsUpdate = true;
    const mesh = new THREE.Mesh(geo, mat);
    mesh.position.set(x, y, z);
    mesh.rotation.y = rotY;
    mesh.castShadow = mesh.receiveShadow = true;
    return mesh;
}

/** Ống trụ đứng theo trục Y (dùng chung 1 geometry đơn vị, chỉ đổi scale) — giàn giáo/cột chống/lan can
 *  có hàng trăm thanh nên tái dùng geometry giúp giảm hẳn bộ nhớ GPU so với tạo mới từng cái. */
export function tube(mat, radius, length, x, y, z, { axis = 'y', rotY = 0, rotZ = 0, hi = false } = {}) {
    const mesh = new THREE.Mesh(hi ? CYL_HI : CYL, mat);
    mesh.scale.set(radius * 2, length, radius * 2);
    mesh.position.set(x, y, z);
    if (axis === 'x') mesh.rotation.z = Math.PI / 2;
    if (axis === 'z') mesh.rotation.x = Math.PI / 2;
    mesh.rotation.y += rotY;
    mesh.rotation.z += rotZ;
    mesh.castShadow = mesh.receiveShadow = true;
    return mesh;
}

/** Bóng tiếp đất giả: một mặt phẳng gradient tối đặt sát dưới vật thể. Shadow map dù bật vẫn luôn hơi
 *  "hụt" ngay tại điểm tiếp xúc (do bias chống răng cưa đẩy bóng ra xa), khiến vật trông như lơ lửng vài
 *  centimet. Vệt tối này lấp đúng khoảng hụt đó — mẹo quen thuộc của game thời gian thực. */
let contactShadowTexture = null;
export function contactShadow(size, x, z, opacity = 0.45, aspect = 1) {
    if (!contactShadowTexture) {
        contactShadowTexture = canvasTexture(128, 128, (ctx, w, h) => {
            const g = ctx.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, w / 2);
            g.addColorStop(0, 'rgba(0,0,0,1)');
            g.addColorStop(0.45, 'rgba(0,0,0,0.65)');
            g.addColorStop(1, 'rgba(0,0,0,0)');
            ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
        }, { srgb: false });
        contactShadowTexture.wrapS = contactShadowTexture.wrapT = THREE.ClampToEdgeWrapping;
    }
    const mesh = new THREE.Mesh(
        new THREE.PlaneGeometry(size, size * aspect),
        new THREE.MeshBasicMaterial({ map: contactShadowTexture, transparent: true, opacity, depthWrite: false, color: 0x2a2118 })
    );
    mesh.rotation.x = -Math.PI / 2;
    mesh.position.set(x, 0.015, z);
    mesh.renderOrder = -1;
    return mesh;
}

// ==================== ĐẠO DIỄN CAMERA & ĐIỂM ĐÁNH DẤU ====================

/**
 * Nối cảnh 3D với lớp giao diện HTML nằm trên nó: chiếu tọa độ 3D của các điểm tình huống ra pixel
 * của marker DOM, và điều khiển camera lia tới điểm mà người học vừa bấm.
 *
 * Tách khỏi từng file bối cảnh vì đây là phần KHÔNG phụ thuộc bối cảnh — công trường hay văn phòng
 * thì phép chiếu, phép kiểm tra che khuất và hoạt cảnh lia camera đều y hệt nhau; chỉ có kích thước
 * không gian (khoảng cách đứng nhìn) là khác, nên được đưa ra tham số.
 *
 * @param {object} option
 * @param {HTMLElement} option.container phần tử bọc canvas — marker được đặt tuyệt đối bên trong nó
 * @param {THREE.Camera} option.camera
 * @param {object} option.controls OrbitControls
 * @param {THREE.Object3D[]} option.occluderList các khối lớn dùng để kiểm tra marker có bị che không.
 *        Giữ THAM CHIẾU tới chính mảng đó, nên file bối cảnh vẫn push thêm sau khi gọi hàm này được.
 * @param {number} option.centerX tâm không gian — dùng khi phải đổi sang hướng nhìn từ ngoài vào
 * @param {number} option.centerZ
 * @param {number} option.focusDistance khoảng cách camera đứng khi lia tới một điểm tình huống
 * @param {number} option.markerScaleBase mẫu số của phép thu nhỏ marker theo khoảng cách
 */
export function createDirector({ container, camera, controls, occluderList,
                                 centerX = 0, centerZ = 0, focusDistance = 17, markerScaleBase = 26 }) {
    const markerList = [];          // { id, position, el }
    const raycaster = new THREE.Raycaster();
    const projected = new THREE.Vector3();
    let frame = 0;

    let tween = null;
    let homePosition = camera.position.clone();
    let homeTarget = controls.target.clone();
    const easeInOut = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

    function tweenCamera(toPosition, toTarget, duration = 1.1) {
        tween = {
            fromPosition: camera.position.clone(), toPosition: toPosition.clone(),
            fromTarget: controls.target.clone(), toTarget: toTarget.clone(),
            elapsed: 0, duration,
        };
        controls.enabled = false;
    }

    function updateTween(delta) {
        if (!tween) return;
        tween.elapsed += delta;
        const t = Math.min(1, tween.elapsed / tween.duration);
        const k = easeInOut(t);
        camera.position.lerpVectors(tween.fromPosition, tween.toPosition, k);
        controls.target.lerpVectors(tween.fromTarget, tween.toTarget, k);
        if (t >= 1) { tween = null; controls.enabled = true; }
    }

    /** Chiếu tọa độ 3D của từng điểm tình huống lên tọa độ pixel của marker DOM.
     *  Ngoài phép chiếu, hàm còn làm 3 việc khiến marker "thuộc về" cảnh 3D chứ không dán lên trên:
     *   - thu nhỏ dần theo khoảng cách (marker ở xa nhỏ hơn marker ở gần);
     *   - xếp chồng theo chiều sâu (z-index) để marker gần đè lên marker xa;
     *   - MỜ ĐI (không ẩn hẳn) khi bị khối lớn che khuất, để người học biết phía sau vật cản còn một
     *     điểm nữa mà xoay camera sang xem; ẩn hẳn thì bài học có thể "mất" một điểm mà không ai biết.
     *     Phép kiểm tra che khuất chạy thưa (4 khung hình/lần) vì raycast là phép đắt nhất trong vòng
     *     lặp, mà vật cản thì gần như không đổi giữa các khung hình liền nhau.
     */
    function updateMarkers() {
        const w = container.clientWidth, h = container.clientHeight;
        const checkOcclusion = (frame % 4 === 0);
        frame++;
        for (const marker of markerList) {
            projected.copy(marker.position).project(camera);
            // z > 1 nghĩa là điểm nằm sau lưng camera — chiếu ra sẽ cho tọa độ lộn ngược, phải ẩn hẳn.
            if (projected.z > 1) { marker.el.style.display = 'none'; continue; }

            const distance = camera.position.distanceTo(marker.position);
            if (checkOcclusion) {
                raycaster.set(camera.position, marker.position.clone().sub(camera.position).normalize());
                const hit = raycaster.intersectObjects(occluderList, true)[0];
                // Chừa 0.9m dung sai: marker gắn ở mép bàn/mép sàn, không có dung sai thì chính vật mà
                // nó đang chỉ vào sẽ tự che nó và marker nhấp nháy liên tục.
                marker.el.classList.toggle('behind', !!hit && hit.distance < distance - 0.9);
            }

            marker.el.style.display = '';
            marker.el.style.left = `${(projected.x * 0.5 + 0.5) * w}px`;
            marker.el.style.top = `${(-projected.y * 0.5 + 0.5) * h}px`;
            const scale = THREE.MathUtils.clamp(markerScaleBase / distance, 0.6, 1.2);
            marker.el.style.setProperty('--hotspot-scale', scale.toFixed(3));
            // Giữ trong dải 1–600: marker gần đè lên marker xa, nhưng TOÀN BỘ marker vẫn nằm dưới lớp
            // giao diện (HUD 800, bảng xử lý 1000 — xem style.css). Nếu để z-index tự do theo khoảng
            // cách thì marker sẽ nổi đè lên cả bảng xử lý tình huống đang mở.
            marker.el.style.zIndex = String(THREE.MathUtils.clamp(600 - Math.round(distance * 8), 1, 600));
        }
    }

    return {
        tweenCamera,
        updateTween,
        updateMarkers,

        /** Đăng ký một marker DOM gắn với tọa độ 3D lấy từ CSDL. */
        addMarker(id, x, y, z, el) {
            markerList.push({ id, position: new THREE.Vector3(x, y, z), el });
        },

        /** Dời một marker đã đăng ký sang tọa độ khác. Dùng khi SOẠN BÀI: người tạo nội dung bấm vào
         *  cảnh để chọn lại vị trí của tình huống đang nhập, marker phải nhảy theo ngay từng cú bấm. */
        moveMarker(id, x, y, z) {
            const marker = markerList.find(m => m.id === id);
            if (marker) marker.position.set(x, y, z);
            return !!marker;
        },

        /** Gỡ toàn bộ marker (kể cả phần tử DOM của chúng) để vẽ lại danh sách từ đầu — cũng dùng khi
         *  soạn bài: lưu/xóa xong một tình huống thì danh sách marker phải dựng lại theo dữ liệu mới. */
        clearMarker() {
            for (const marker of markerList) marker.el.remove();
            markerList.length = 0;
        },

        /** Đặt góc nhìn "về tổng thể" mà nút Góc nhìn tổng thể sẽ quay lại. */
        setHome(position, target) {
            homePosition = position.clone();
            homeTarget = target.clone();
        },

        /** Lia camera tới quan sát một điểm tình huống.
         *  Ưu tiên GIỮ NGUYÊN hướng nhìn hiện tại (chỉ tiến lại gần) để người học không bị mất phương
         *  hướng. Nhưng nếu ở hướng đó có khối lớn chắn giữa — rất hay xảy ra vì nhiều điểm nằm bên kia
         *  một bức tường — thì đổi sang hướng NHÌN TỪ NGOÀI VÀO: đứng ở phía đối diện tâm không gian so
         *  với điểm đó, đúng như người đi kiểm tra sẽ vòng ra ngoài để nhìn cho rõ. Không có bước này,
         *  bấm vào một điểm ở phòng phía sau sẽ cho ra khung hình nhìn xuyên qua tường. */
        focus(x, y, z) {
            const target = new THREE.Vector3(x, y, z);
            const keepDirection = camera.position.clone().sub(controls.target).normalize();
            const outward = new THREE.Vector3(x - centerX, 0, z - centerZ);
            if (outward.lengthSq() < 0.04) outward.set(0.3, 0, 1);
            outward.normalize().setY(0.45).normalize();

            const placeAt = (direction) => {
                const from = target.clone().addScaledVector(direction, focusDistance);
                from.y = Math.max(from.y, y + 2.5, 3);
                return from;
            };
            let from = placeAt(keepDirection);
            raycaster.set(from, target.clone().sub(from).normalize());
            const hit = raycaster.intersectObjects(occluderList, true)[0];
            if (hit && hit.distance < from.distanceTo(target) - 1.2) from = placeAt(outward);

            tweenCamera(from, target, 1.0);
        },

        resetCamera() { tweenCamera(homePosition, homeTarget, 1.2); },
    };
}

/**
 * Bộ điều khiển chung của TRÌNH XEM 3D (tự xoay / độ sáng / chụp khung hình), dùng chung cho mọi bối cảnh:
 * cảnh dựng bằng code (sim-scene.js, sim-hr-scene.js) lẫn cảnh dựng từ tệp tải lên (sim-asset-scene.js).
 *
 * Gom về đây thay vì viết lại ở từng cảnh vì thanh công cụ trên trang học là MỘT: nếu mỗi cảnh tự cài một
 * kiểu, cùng một nút bấm sẽ có tác dụng khác nhau tùy bối cảnh — và đó chính là loại khác biệt người dùng
 * không bao giờ đoán trước được.
 *
 * @param {object} option
 * @param {THREE.WebGLRenderer} option.renderer
 * @param {OrbitControls} option.controls
 * @param {THREE.Scene} option.scene
 * @param {THREE.Camera} option.camera
 * @param {number} [option.baseExposure] độ phơi sáng gốc của cảnh — mức sáng được nhân THEO TỈ LỆ với số
 *        này, vì mỗi cảnh có một mức gốc riêng (công trường 0.34, văn phòng 1.02); đặt cùng một giá trị
 *        tuyệt đối cho cả hai sẽ làm cháy sáng cảnh này và tối đen cảnh kia.
 * @param {boolean} [option.canAutoRotate] cảnh có cho tự xoay quanh tâm không.
 */
export function createViewerControl({ renderer, controls, scene, camera, baseExposure = 1, canAutoRotate = true }) {
    return {
        canAutoRotate,

        /** HÀM chứ không phải getter: bộ điều khiển này được đưa vào cảnh bằng toán tử spread ({...}), mà
         *  spread ĐỌC getter một lần rồi chép lại GIÁ TRỊ — để dạng getter thì cảnh nào cũng nhận một hằng
         *  số false và nút "Tự xoay" bật được nhưng không bao giờ tắt được. */
        isAutoRotating: () => !!controls.autoRotate,

        setAutoRotate(value) {
            if (!canAutoRotate) return false;
            controls.autoRotate = !!value;
            controls.autoRotateSpeed = 1.6;
            return controls.autoRotate;
        },

        /** @param {number} level hệ số so với độ sáng gốc: 1 = như thiết kế, >1 sáng hơn, <1 tối hơn. */
        setBrightness(level) {
            const factor = Math.max(0.3, Math.min(2.2, Number(level) || 1));
            renderer.toneMappingExposure = baseExposure * factor;
            return factor;
        },

        /** Chụp khung hình hiện tại thành data URL PNG. Phải render ngay trước khi đọc — WebGL xóa bộ đệm
         *  khung hình sau mỗi lần vẽ nên đọc ở thời điểm khác sẽ ra ảnh đen. */
        capture(maxWidth = 640) {
            renderer.render(scene, camera);
            const source = renderer.domElement;
            const scale = Math.min(1, maxWidth / (source.width || maxWidth));
            const canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(source.width * scale));
            canvas.height = Math.max(1, Math.round(source.height * scale));
            canvas.getContext('2d').drawImage(source, 0, 0, canvas.width, canvas.height);
            return canvas.toDataURL('image/png');
        },
    };
}
