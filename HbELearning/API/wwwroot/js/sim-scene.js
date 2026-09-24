// File: wwwroot/js/sim-scene.js
// ============================================================================================
// THẾ GIỚI 3D CỦA TRANG MÔ PHỎNG (pages/simulation.html)
//
// Tách riêng khỏi simulation.html vì hai lý do: (1) trang HTML chỉ còn lo phần nghiệp vụ (gọi API,
// chấm điểm, bảng xử lý tình huống) nên đọc được trong một màn hình; (2) phần đồ họa nặng nhất — dựng
// cảnh, vật liệu, tải model — nằm gọn một chỗ để chỉnh sửa/đo hiệu năng mà không đụng vào logic bài học.
//
// Đây là NGOẠI LỆ duy nhất của dự án dùng ES module (Three.js không còn phát hành bản UMD/global), khớp
// với importmap khai báo trong simulation.html.
//
// TRIẾT LÝ DỰNG CẢNH
// -------------------
// Cảnh chia làm 2 loại vật thể:
//   * MODEL THẬT (wwwroot/models/*.glb) — cẩu tháp và công nhân. Đây là hai thứ mà khối hình học tự dựng
//     bằng code không bao giờ giả được: cẩu tháp có hàng trăm thanh giằng, công nhân có hình người thật
//     kèm animation. Model đã được tối ưu sẵn (xem models/README.txt) nên tải nhanh.
//   * DỰNG BẰNG CODE — nhà đang xây, giàn giáo, hàng rào, bãi vật liệu, xe tải, biển báo... Những thứ này
//     PHẢI dựng bằng code vì hình dạng của chúng chính là NỘI DUNG BÀI HỌC: giàn giáo phải thiếu đúng
//     thanh giằng chéo ở đúng nhịp, vật liệu phải chất cao sát lối đi, lối người đi bộ phải cụt ngang
//     đường xe chạy. Model tải sẵn ngoài mạng không bao giờ "sai" đúng chỗ mình cần.
//
// Chất lượng hình ảnh dựa vào 4 thứ, theo thứ tự đóng góp giảm dần:
//   1. Ánh sáng dựa trên môi trường (IBL): sinh env map từ chính bầu trời Sky bằng PMREMGenerator, nên
//      kim loại phản chiếu đúng màu trời/đất thay vì đen kịt như khi chỉ có đèn định hướng.
//   2. Vật liệu có vân + normal map sinh bằng thuật toán (không tải ảnh ngoài, không tốn băng thông):
//      đất, bê tông, tôn, gỗ, gạch đều có nổi/lõm thật thay vì mảng màu phẳng "nhựa 3D".
//   3. Bóng đổ: shadow map phân giải cao nhưng khung chiếu (frustum) bó sát khu vực có người/vật, nên
//      từng pixel bóng dày đặc chi tiết thay vì răng cưa.
//   4. Lọc dị hướng (anisotropy) tối đa cho texture mặt đất — mặt phẳng lớn nhìn xiên mà không có nó sẽ
//      nhòe thành một vệt xám ở xa, đúng thứ khiến cảnh trông "mờ" chứ không "nét".
// ============================================================================================

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { Sky } from 'three/addons/objects/Sky.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { clone as cloneSkeleton } from 'three/addons/utils/SkeletonUtils.js';
// Bộ dựng cảnh dùng chung với các bối cảnh mô phỏng khác (xem js/sim-kit.js).
import {
    fbm, makeSurface, canvasTexture, heightToNormalTexture,
    box, texBox, tube, contactShadow, createDirector, createViewerControl,
} from '/js/sim-kit.js';

// ==================== HẰNG SỐ BỐ CỤC CÔNG TRƯỜNG ====================
// 1 đơn vị = 1 mét. Các tọa độ dưới đây phải khớp với PositionX/Y/Z của ScenarioNode trong CSDL
// (xem Data/DbSeeder.cs) — điểm nguy cơ được chiếu lên màn hình theo đúng tọa độ đó, nên nếu dời vật thể
// mà quên dời theo thì marker sẽ chỉ vào chỗ trống.
const SITE = {
    groundSize: 220,          // nền đất kéo rộng ra tận chân trời để không thấy "mép thế giới"
    fenceHalf: 26,            // hàng rào tôn quây quanh, tâm ở gốc tọa độ
    buildingMinX: -5.5, buildingMaxX: 3.5,
    buildingMinZ: -0.5, buildingMaxZ: 5.5,
    floorHeight: 3,
    floorCount: 5,
    scaffoldX: -6.05,         // mặt trước tòa nhà (hướng -X) — nơi có giàn giáo, hazard 1 & 2
    craneBase: new THREE.Vector3(-14, 0, -4),
    craneScale: 0.58,
    hookAnchor: new THREE.Vector3(-2, 0, 7),  // tải treo phải rơi đúng trên hazard 7 (-2, 2, 7)
    loadY: 3.2,
    // Xe tải đỗ sao cho ĐUÔI xe (thùng ben, hướng -Z) dừng ngay trước điểm nguy cơ 6 tại z = -3: marker
    // phải nằm ở khoảng trống sau xe — nơi tài xế không nhìn thấy — chứ không lọt vào trong khối thùng xe.
    truck: { x: 9, z: 0.4, rotY: 0 },
    haulRoadZ: -8,            // trục đường xe chạy trong công trường (hazard 8)
    crossingX: 12.3,          // vạch sơn lối đi bộ cắt ngang đường xe (hazard 8)
};

const HARD_HAT = { white: 0xf1f5f9, yellow: 0xfacc15, orange: 0xf97316, blue: 0x38bdf8 };

function brickTexture(repeat = [1, 1]) {
    return canvasTexture(256, 256, (ctx, w, h) => {
        ctx.fillStyle = '#8d8377'; ctx.fillRect(0, 0, w, h);           // vữa
        const rows = 8, bh = h / rows, bw = w / 4;
        for (let r = 0; r < rows; r++) {
            const offset = (r % 2) * bw / 2;
            for (let c = -1; c < 5; c++) {
                const x = c * bw + offset, y = r * bh;
                const tone = 150 + Math.random() * 45;
                ctx.fillStyle = `rgb(${tone | 0}, ${(tone * 0.52) | 0}, ${(tone * 0.36) | 0})`;
                ctx.fillRect(x + 2, y + 2, bw - 4, bh - 4);
            }
        }
    }, { repeat });
}

/** Tôn sóng: chỉ cần một normal map hình sin là đủ — hình học vẫn là mặt phẳng phẳng lì nên gần như miễn
 *  phí, nhưng ánh sáng quét dọc theo sóng cho ra đúng cảm giác hàng rào tôn ngoài công trường. */
function corrugatedNormalTexture(repeat = [1, 1]) {
    const size = 128;
    const height = new Float32Array(size * size);
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) height[y * size + x] = 0.5 + 0.5 * Math.sin((x / size) * Math.PI * 2 * 8);
    }
    const tex = heightToNormalTexture(height, size, 42, 1);
    tex.repeat.set(repeat[0], repeat[1]);
    return tex;
}

/** Biển cảnh báo có chữ tiếng Việt — chi tiết nhỏ nhưng là thứ khiến người học nhận ra ngay đây là công
 *  trường Việt Nam chứ không phải một cảnh 3D chung chung. */
function signTexture(lines, { bg = '#facc15', fg = '#111827', accent = '#dc2626' } = {}) {
    return canvasTexture(256, 256, (ctx, w, h) => {
        ctx.fillStyle = bg; ctx.fillRect(0, 0, w, h);
        ctx.strokeStyle = accent; ctx.lineWidth = 12; ctx.strokeRect(8, 8, w - 16, h - 16);
        // Tam giác cảnh báo
        ctx.fillStyle = accent;
        ctx.beginPath(); ctx.moveTo(w / 2, 42); ctx.lineTo(w / 2 + 34, 96); ctx.lineTo(w / 2 - 34, 96); ctx.closePath(); ctx.fill();
        ctx.fillStyle = bg; ctx.font = 'bold 34px sans-serif'; ctx.textAlign = 'center';
        ctx.fillText('!', w / 2, 93);
        ctx.fillStyle = fg; ctx.textAlign = 'center';
        lines.forEach((line, i) => {
            ctx.font = `bold ${line.length > 14 ? 22 : 28}px sans-serif`;
            ctx.fillText(line, w / 2, 140 + i * 34);
        });
    });
}


// ==================== TẠO THẾ GIỚI ====================

/**
 * Dựng toàn bộ cảnh 3D vào `container` và trả về đối tượng điều khiển cho trang gọi.
 * @param {HTMLElement} container phần tử bọc canvas (phải có kích thước > 0)
 * @param {(text: string) => void} onProgress báo tiến độ để hiện lên màn hình chờ
 */
export async function createSimWorld(container, onProgress = () => {}) {
    // ---------- Renderer ----------
    const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.setSize(container.clientWidth, container.clientHeight);
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    // ACES + sRGB: cùng công thức phim/game dùng để nén dải sáng rộng ngoài trời xuống màn hình mà vẫn
    // giữ được chi tiết vùng cháy sáng, thay vì màu bệt của render mặc định.
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    // Bầu trời Sky phát ra độ sáng dải rộng (HDR) chứ không phải màu 0-1: để exposure ~1 thì trời cháy
    // trắng xóa và cả cảnh bị "sương mù" mất tương phản. 0.34 là mức đưa trời về đúng xanh, bù lại phải
    // tăng cường độ đèn mặt trời bên dưới (9.8) cho vật thể không bị tối theo.
    renderer.toneMappingExposure = 0.34;
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    container.insertBefore(renderer.domElement, container.firstChild);

    const maxAniso = renderer.capabilities.getMaxAnisotropy();

    // ---------- Scene / Camera / Controls ----------
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(44, container.clientWidth / container.clientHeight, 0.25, 900);
    camera.position.set(-8, 15, 31);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.target.set(-2, 7, 0);
    controls.enableDamping = true;
    controls.dampingFactor = 0.06;
    controls.minDistance = 7;
    controls.maxDistance = 60;
    controls.maxPolarAngle = Math.PI * 0.487;  // chặn không cho lọt xuống dưới mặt đất
    controls.update();

    // ---------- Bầu trời + ánh sáng môi trường ----------
    onProgress('Đang dựng bầu trời và ánh sáng...');
    const sky = new Sky();
    sky.scale.setScalar(8000);
    // Nắng chếch từ sau-trái so với góc nhìn mặc định: bóng đổ xiên về phía người xem nên mọi khối đều
    // có mặt sáng/mặt tối rõ ràng (nắng chính diện làm cảnh bẹt, nắng ngược làm mất hết chi tiết).
    const sunElevation = 40, sunAzimuth = 235;
    const sunDirection = new THREE.Vector3().setFromSphericalCoords(
        1, THREE.MathUtils.degToRad(90 - sunElevation), THREE.MathUtils.degToRad(sunAzimuth));
    sky.material.uniforms.turbidity.value = 3.2;
    sky.material.uniforms.rayleigh.value = 3.0;
    sky.material.uniforms.mieCoefficient.value = 0.005;
    sky.material.uniforms.mieDirectionalG.value = 0.8;
    sky.material.uniforms.sunPosition.value.copy(sunDirection);

    // Sinh env map từ chính bầu trời vừa dựng: mọi bề mặt kim loại (cẩu tháp, giàn giáo, thùng xe) từ đây
    // phản chiếu đúng màu trời phía trên và màu đất phía dưới. Đây là bước đổi cảm giác lớn nhất giữa
    // "hình 3D làm bài tập" và "ảnh render" — không có nó thì metalness cao chỉ ra màu đen.
    const pmrem = new THREE.PMREMGenerator(renderer);
    pmrem.compileEquirectangularShader();
    const envScene = new THREE.Scene();
    envScene.add(sky);
    const envTarget = pmrem.fromScene(envScene, 0, 0.1, 20000);
    scene.environment = envTarget.texture;
    scene.add(sky);
    pmrem.dispose();

    // Sương mù xa dần nhòe vào màu trời — thiếu nó thì mép nền đất cắt ngang trời thành một đường thẳng
    // lộ liễu, phá hết cảm giác không gian mở.
    // Sương mù bắt đầu ngay sau hàng rào (26m) và đặc hẳn ở 130m: nền đất phẳng trải rộng mà không nhòe
    // dần sẽ cắt ngang bầu trời thành một đường thẳng lộ liễu, phá ngay cảm giác không gian mở.
    scene.fog = new THREE.Fog(0xa9c3da, 40, 150);

    scene.add(new THREE.HemisphereLight(0xbcd8f7, 0x6d6047, 1.7));

    const sun = new THREE.DirectionalLight(0xfff0d6, 9.8);
    sun.position.copy(sunDirection).multiplyScalar(90);
    sun.castShadow = true;
    sun.shadow.mapSize.set(4096, 4096);
    // Khung chiếu bóng bó sát khu vực có hoạt động (±28m) thay vì bao cả nền đất 130m: cùng một shadow map
    // 4096², thu hẹp khung lại làm mật độ pixel bóng tăng gấp ~20 lần — đây mới là thứ quyết định bóng
    // "sắc nét" hay "răng cưa", không phải con số 4096.
    sun.shadow.camera.left = -32; sun.shadow.camera.right = 32;
    sun.shadow.camera.top = 30; sun.shadow.camera.bottom = -30;
    sun.shadow.camera.near = 30; sun.shadow.camera.far = 190;
    sun.shadow.bias = -0.0008;
    sun.shadow.normalBias = 0.035;
    sun.shadow.radius = 1.4;
    sun.target.position.set(-1, 5, 2);
    scene.add(sun, sun.target);

    // Đèn hắt ngược từ phía đối diện mặt trời (không đổ bóng): giả lập ánh sáng khuếch tán từ trời và mặt
    // đất phản chiếu lại, giữ cho vùng khuất không bị đen đặc mất hết chi tiết.
    const bounce = new THREE.DirectionalLight(0xa9c8ea, 1.4);
    bounce.position.set(-sun.position.x, 22, -sun.position.z);
    scene.add(bounce);

    // ---------- Vật liệu dùng chung ----------
    onProgress('Đang sinh vật liệu bề mặt...');
    const dirt = makeSurface({
        size: 256, octaveList: [[4, 1], [8, 0.55], [16, 0.28], [32, 0.14], [64, 0.07]],
        darkColor: [132, 110, 80], lightColor: [178, 152, 114],
        repeat: 30, normalStrength: 55, roughLow: 0.82, roughHigh: 1.0, speckle: 0.02,
    });
    // repeat = 1 cho những vật liệu dùng với texBox(): số lần lặp đã nằm sẵn trong UV tính theo mét thật.
    // Bê tông: chỉ dùng nhiễu tần số CAO. Nếu để cả lớp tần số thấp thì mảng sáng/tối cỡ nửa mét sẽ hiện
    // ra như vân đá hoa cương — bê tông thật chỉ có hạt mịn cộng vài vệt ố, không có vân lớn.
    const concrete = makeSurface({
        size: 256, octaveList: [[16, 1], [32, 0.55], [64, 0.3], [128, 0.16]],
        darkColor: [112, 113, 112], lightColor: [141, 142, 139],
        repeat: 1, normalStrength: 12, roughLow: 0.86, roughHigh: 1.0,
    });
    const rustySteel = makeSurface({
        size: 128, octaveList: [[6, 1], [16, 0.45], [40, 0.2]],
        darkColor: [96, 62, 40], lightColor: [156, 112, 74],
        repeat: 1, normalStrength: 14, roughLow: 0.55, roughHigh: 0.85,
    });
    const timber = makeSurface({
        size: 128, octaveList: [[2, 1], [40, 0.35], [90, 0.18]],
        darkColor: [140, 104, 58], lightColor: [206, 168, 112],
        repeat: 1, normalStrength: 12, roughLow: 0.75, roughHigh: 0.95,
    });

    [dirt, concrete, rustySteel, timber].forEach(s => {
        s.map.anisotropy = maxAniso; s.normalMap.anisotropy = maxAniso; s.roughnessMap.anisotropy = maxAniso;
    });

    const MAT = {
        ground: new THREE.MeshStandardMaterial({
            map: dirt.map, normalMap: dirt.normalMap, roughnessMap: dirt.roughnessMap,
            normalScale: new THREE.Vector2(1.4, 1.4), roughness: 1, metalness: 0,
        }),
        concrete: new THREE.MeshStandardMaterial({
            map: concrete.map, normalMap: concrete.normalMap, roughnessMap: concrete.roughnessMap,
            roughness: 1, metalness: 0,
        }),
        // Dầm biên dùng chung vân bê tông nhưng tối hơn một chút — ván khuôn dầm và ván khuôn sàn khác
        // nhau nên bê tông ra màu không bao giờ đều tăm tắp, đây là chi tiết nhỏ nhưng phá được cảm giác
        // "cả tòa nhà đúc từ một khối nhựa".
        concreteDark: new THREE.MeshStandardMaterial({
            color: 0xdcdedc, map: concrete.map, normalMap: concrete.normalMap,
            roughnessMap: concrete.roughnessMap, roughness: 1, metalness: 0,
        }),
        rust: new THREE.MeshStandardMaterial({
            map: rustySteel.map, normalMap: rustySteel.normalMap, roughness: 0.78, metalness: 0.35,
        }),
        timber: new THREE.MeshStandardMaterial({
            map: timber.map, normalMap: timber.normalMap, roughness: 0.86, metalness: 0,
        }),
        brick: new THREE.MeshStandardMaterial({ map: brickTexture(), roughness: 0.92, metalness: 0 }),
        // Giàn giáo thép sơn cam: metalness vừa phải để bắt được env map (ánh trời) mà không thành gương.
        scaffold: new THREE.MeshStandardMaterial({ color: 0xe8712c, roughness: 0.45, metalness: 0.65 }),
        steel: new THREE.MeshStandardMaterial({ color: 0x9aa2a8, roughness: 0.38, metalness: 0.85 }),
        steelDark: new THREE.MeshStandardMaterial({ color: 0x2b3138, roughness: 0.5, metalness: 0.7 }),
        rebar: new THREE.MeshStandardMaterial({ color: 0x6f6a60, roughness: 0.62, metalness: 0.7 }),
        hoarding: new THREE.MeshStandardMaterial({
            color: 0x8fa6bd, roughness: 0.5, metalness: 0.55,
            normalMap: corrugatedNormalTexture(), normalScale: new THREE.Vector2(1.1, 1.1),
        }),
        containerBlue: new THREE.MeshStandardMaterial({
            color: 0x1f5f8b, roughness: 0.55, metalness: 0.5,
            normalMap: corrugatedNormalTexture(), normalScale: new THREE.Vector2(1.2, 1.2),
        }),
        containerOrange: new THREE.MeshStandardMaterial({
            color: 0xb45309, roughness: 0.58, metalness: 0.45,
            normalMap: corrugatedNormalTexture(), normalScale: new THREE.Vector2(1.2, 1.2),
        }),
        hivis: new THREE.MeshStandardMaterial({ color: 0xff7a00, roughness: 0.6, metalness: 0.05 }),
        reflectiveBand: new THREE.MeshStandardMaterial({ color: 0xe5e7eb, roughness: 0.25, metalness: 0.35 }),
        plastic: new THREE.MeshStandardMaterial({ color: 0xea580c, roughness: 0.55, metalness: 0 }),
        plasticWhite: new THREE.MeshStandardMaterial({ color: 0xf8fafc, roughness: 0.5, metalness: 0 }),
        // Lưới an toàn: hai mặt + trong suốt, mắt lưới giả bằng alphaMap kẻ ô để nhìn xuyên thấy giàn giáo
        // phía sau — nếu để đặc thì lưới che mất đúng thứ bài học muốn người học nhìn thấy.
        safetyNet: new THREE.MeshStandardMaterial({
            color: 0x2f9e5f, roughness: 0.9, metalness: 0, side: THREE.DoubleSide,
            transparent: true, opacity: 0.55, depthWrite: false,
        }),
        glass: new THREE.MeshStandardMaterial({ color: 0x9dc7e8, roughness: 0.08, metalness: 0.25,
            transparent: true, opacity: 0.62 }),
        water: new THREE.MeshStandardMaterial({ color: 0x3f556b, roughness: 0.04, metalness: 0.55,
            transparent: true, opacity: 0.85 }),
        // Đường công vụ là đất lu lèn trộn đá dăm, không phải nhựa đường: dùng lại chính vân nền đất nhưng
        // nhân màu tối và mịn hơn. Một mảng xám phẳng ở đây sẽ đọc nhầm thành "bóng đổ" chứ không ra đường.
        tarmac: new THREE.MeshStandardMaterial({
            color: 0x9d9081, map: dirt.map, normalMap: dirt.normalMap, roughnessMap: dirt.roughnessMap,
            normalScale: new THREE.Vector2(0.6, 0.6), roughness: 1, metalness: 0,
        }),
        paintYellow: new THREE.MeshStandardMaterial({ color: 0xfacc15, roughness: 0.75, metalness: 0 }),
        cableSheath: new THREE.MeshStandardMaterial({ color: 0x111318, roughness: 0.7, metalness: 0.1 }),
        copper: new THREE.MeshStandardMaterial({ color: 0xd97b3a, roughness: 0.3, metalness: 0.9,
            emissive: 0x7c2d12, emissiveIntensity: 0.4 }),
    };

    // Đống đất đào là khối đa diện (không phải hộp) nên không dùng được UV tính theo mét của texBox();
    // ở đây chỉ cần giảm số lần lặp xuống cho khớp cỡ đống đất. Texture clone chỉ sao chép tham số, vẫn
    // dùng chung ảnh gốc nên gần như không tốn thêm bộ nhớ.
    MAT.dirtHeap = MAT.ground.clone();
    ['map', 'normalMap', 'roughnessMap'].forEach(slot => {
        MAT.dirtHeap[slot] = MAT.dirtHeap[slot].clone();
        MAT.dirtHeap[slot].repeat.set(3, 3);
        MAT.dirtHeap[slot].needsUpdate = true;
    });

    // Danh sách vật cản dùng cho phép thử che khuất của marker điểm nguy cơ (xem createDirector trong js/sim-kit.js).
    // Chỉ gồm KHỐI LỚN — không đưa cọc tiêu, lưới, hạt bụi vào, nếu không marker sẽ nhấp nháy liên tục
    // mỗi khi có một thanh giàn giáo mảnh quét ngang tia kiểm tra.
    const occluderList = [];
    const add = (obj, { occlude = false } = {}) => { scene.add(obj); if (occlude) occluderList.push(obj); return obj; };

    // ---------- Nền đất, đường xe, lối đi bộ ----------
    onProgress('Đang san nền công trường...');
    // Nền đất phải lặp texture rất nhiều lần mới đủ chi tiết khi nhìn gần, nhưng lặp nhiều thì ở xa lộ ra
    // đúng cái lưới ô vuông đều tăm tắp — lỗi kinh điển của mọi mặt phẳng lớn trong đồ họa thời gian thực.
    // Cách chữa rẻ nhất: tô MÀU ĐỈNH biến thiên chậm (mảng sáng/tối cỡ vài chục mét) đè lên texture lặp;
    // mắt bắt mảng lớn trước, nên nhịp lặp nhỏ bên dưới không còn đọc ra thành lưới nữa.
    const groundSeg = 64;
    const groundGeo = new THREE.PlaneGeometry(SITE.groundSize, SITE.groundSize, groundSeg, groundSeg);
    const macroNoise = fbm(groundSeg + 1, [[3, 1], [7, 0.6], [13, 0.35], [26, 0.18]]);
    const groundColor = new Float32Array((groundSeg + 1) * (groundSeg + 1) * 3);
    for (let i = 0; i < macroNoise.length; i++) {
        const shade = 0.62 + macroNoise[i] * 0.78;
        groundColor[i * 3] = shade;
        groundColor[i * 3 + 1] = shade * 0.985;
        groundColor[i * 3 + 2] = shade * 0.95;
    }
    groundGeo.setAttribute('color', new THREE.BufferAttribute(groundColor, 3));
    MAT.ground.vertexColors = true;
    const ground = new THREE.Mesh(groundGeo, MAT.ground);
    ground.rotation.x = -Math.PI / 2;
    ground.receiveShadow = true;
    add(ground, { occlude: true });

    // Đường xe cơ giới chạy trong công trường: dải đất lu lèn sẫm màu. Hazard 8 nói "lối xe trùng hoàn
    // toàn lối người đi bộ" — nên vạch sơn đi bộ bên dưới CỐ TÌNH cắt ngang đúng dải này và dừng lại đột
    // ngột, để người học nhìn thấy lỗi thay vì chỉ đọc mô tả.
    const haulRoad = new THREE.Mesh(new THREE.PlaneGeometry(50, 6.5), MAT.tarmac);
    haulRoad.rotation.x = -Math.PI / 2;
    haulRoad.position.set(2, 0.012, SITE.haulRoadZ);
    haulRoad.receiveShadow = true;
    scene.add(haulRoad);

    // Nhánh rẽ vào bãi tập kết — xe tải đang lùi ngược theo nhánh này ra đường chính (hazard 6)
    const spur = new THREE.Mesh(new THREE.PlaneGeometry(5.6, 12), MAT.tarmac);
    spur.rotation.x = -Math.PI / 2;
    spur.position.set(9, 0.013, -1.5);
    spur.receiveShadow = true;
    scene.add(spur);

    // Vạch sơn nằm NGANG so với hướng người đi bộ (người cắt ngang theo trục Z, vạch kẻ theo trục X) —
    // đúng quy cách vạch qua đường; và vạch chạy thẳng qua đường xe rồi hết, không rào, không biển báo,
    // đó chính là lỗi mà hazard 8 nói tới.
    for (let i = 0; i < 8; i++) {
        const stripe = new THREE.Mesh(new THREE.PlaneGeometry(2.6, 0.5), MAT.paintYellow);
        stripe.rotation.x = -Math.PI / 2;
        stripe.position.set(SITE.crossingX, 0.024, -11.6 + i * 1.05);
        stripe.receiveShadow = true;
        scene.add(stripe);
    }

    // Vũng nước sau mưa (bối cảnh của hazard 3 — dây điện hở cạnh nước đọng)
    [[5.6, -4.0, 1.35], [13.5, -8.6, 1.1], [-8.5, 1.4, 0.9]].forEach(([x, z, r]) => {
        const puddle = new THREE.Mesh(new THREE.CircleGeometry(r, 22), MAT.water);
        puddle.rotation.x = -Math.PI / 2;
        puddle.position.set(x, 0.03, z);
        puddle.receiveShadow = true;
        scene.add(puddle);
    });

    // Đống đất/cát đào lên — icosahedron thô (detail 0) bóp bẹt và kéo lệch mỗi đống một kiểu: mặt phẳng
    // to bản của khối 20 mặt đọc ra thành đống đất đổ có sườn dốc, tự nhiên hơn hình nón đều tăm tắp.
    [[-21, -19, 3.0, 1.3], [-20, 9, 2.2, 0.8], [17, 12, 2.6, 1.1]].forEach(([x, z, r, stretch]) => {
        const heap = new THREE.Mesh(new THREE.IcosahedronGeometry(r, 0), MAT.dirtHeap);
        heap.position.set(x, r * 0.2, z);
        heap.scale.set(stretch, 0.4, 1);
        heap.rotation.y = Math.random() * Math.PI;
        heap.castShadow = heap.receiveShadow = true;
        scene.add(heap);
    });

    // ---------- Hàng rào tôn quây công trường ----------
    const H = SITE.fenceHalf;
    [[0, -H, 0], [0, H, 0], [-H, 0, Math.PI / 2], [H, 0, Math.PI / 2]].forEach(([x, z, rotY]) => {
        const panel = texBox(MAT.hoarding, H * 2, 2.4, 0.08, x, 1.25, z, rotY, 1.0);
        scene.add(panel);
        // Dải xanh chạy dọc mép trên — hàng rào công trường ở Việt Nam gần như luôn có dải màu/logo nhà thầu
        scene.add(texBox(MAT.containerBlue, H * 2, 0.35, 0.1, x, 2.28, z, rotY, 1.0));
        for (let i = -H + 2; i < H; i += 4) {
            const px = rotY === 0 ? i : x, pz = rotY === 0 ? z : i;
            scene.add(tube(MAT.steelDark, 0.06, 2.6, px, 1.3, pz));
        }
    });

    // ---------- Tòa nhà đang xây ----------
    // Dựng theo đúng cách công trình thật mọc lên: cột + sàn + dầm biên, các tầng ĐỂ TRỐNG chưa xây tường.
    // Khối rỗng như vậy vừa đúng thực tế vừa giải quyết một vấn đề kỹ thuật: marker của hazard 1 và 2 nằm
    // ở tọa độ bên trong khối nhà — nếu dựng nhà bằng hộp đặc thì marker sẽ bị chính tòa nhà che mất.
    onProgress('Đang dựng khung nhà và giàn giáo...');
    const bMinX = SITE.buildingMinX, bMaxX = SITE.buildingMaxX;
    const bMinZ = SITE.buildingMinZ, bMaxZ = SITE.buildingMaxZ;
    const bW = bMaxX - bMinX, bD = bMaxZ - bMinZ;
    const siteCenterX = (bMinX + bMaxX) / 2, siteCenterZ = (bMinZ + bMaxZ) / 2;
    const columnXList = [bMinX + 0.3, bMinX + 3.2, bMaxX - 3.2, bMaxX - 0.3];
    const columnZList = [bMinZ + 0.3, (bMinZ + bMaxZ) / 2, bMaxZ - 0.3];

    const building = new THREE.Group();
    scene.add(building);

    // Móng/nền tầng trệt
    const slabBase = texBox(MAT.concrete, bW + 1.4, 0.35, bD + 1.4, (bMinX + bMaxX) / 2, 0.17, (bMinZ + bMaxZ) / 2);
    building.add(slabBase);
    occluderList.push(slabBase);

    for (let floor = 0; floor < SITE.floorCount; floor++) {
        const y0 = floor * SITE.floorHeight;
        // Cột
        columnXList.forEach(cx => columnZList.forEach(cz => {
            building.add(texBox(MAT.concrete, 0.42, SITE.floorHeight, 0.42, cx, y0 + SITE.floorHeight / 2, cz));
        }));
        // Sàn tầng trên + dầm biên
        const slabY = y0 + SITE.floorHeight;
        const slab = texBox(MAT.concrete, bW, 0.28, bD, (bMinX + bMaxX) / 2, slabY, (bMinZ + bMaxZ) / 2);
        building.add(slab);
        occluderList.push(slab);
        building.add(texBox(MAT.concreteDark, bW, 0.5, 0.22, (bMinX + bMaxX) / 2, slabY + 0.25, bMinZ));
        building.add(texBox(MAT.concreteDark, bW, 0.5, 0.22, (bMinX + bMaxX) / 2, slabY + 0.25, bMaxZ));
        building.add(texBox(MAT.concreteDark, 0.22, 0.5, bD, bMinX, slabY + 0.25, (bMinZ + bMaxZ) / 2));
        building.add(texBox(MAT.concreteDark, 0.22, 0.5, bD, bMaxX, slabY + 0.25, (bMinZ + bMaxZ) / 2));

        // Tường gạch xây dở: tầng càng thấp xây càng nhiều — đúng trình tự thi công từ dưới lên, và cho
        // công trình một dáng "đang lớn dần" thay vì khung xương đều tăm tắp từ trên xuống dưới.
        const builtRatio = Math.max(0, 1 - floor * 0.32);
        if (builtRatio > 0.05) {
            const wallH = SITE.floorHeight * builtRatio * 0.9;
            const wall = texBox(MAT.brick, 0.2, wallH, bD * 0.62, bMaxX - 0.05, y0 + wallH / 2 + 0.2, (bMinZ + bMaxZ) / 2 - 0.4, 1.2);
            building.add(wall);
            occluderList.push(wall);
            if (floor < 2) {
                building.add(texBox(MAT.brick, bW * 0.5, wallH * 0.8, 0.2, bMinX + bW * 0.28, y0 + wallH * 0.4 + 0.2, bMaxZ - 0.05, 1.2));
            }
        }
    }

    // Thép chờ nhô lên đỉnh cột tầng trên cùng — dấu hiệu kinh điển của công trình còn đang thi công
    const topY = SITE.floorCount * SITE.floorHeight + 0.14;
    columnXList.forEach(cx => columnZList.forEach(cz => {
        for (let i = 0; i < 4; i++) {
            const ox = (i % 2 ? 0.13 : -0.13), oz = (i < 2 ? 0.13 : -0.13);
            building.add(tube(MAT.rebar, 0.022, 1.5, cx + ox, topY + 0.75, cz + oz));
        }
    }));

    // Cột chống (giáo chống) đỡ ván khuôn sàn đang đổ ở tầng trên cùng
    for (let i = 0; i < 8; i++) {
        const px = bMinX + 0.8 + (i % 4) * 1.5, pz = bMinZ + 1.2 + Math.floor(i / 4) * 3;
        building.add(tube(MAT.steel, 0.05, SITE.floorHeight - 0.3,
            px, (SITE.floorCount - 1) * SITE.floorHeight + SITE.floorHeight / 2, pz));
    }
    building.add(texBox(MAT.timber, bW * 0.55, 0.08, bD * 0.5, bMinX + bW * 0.35,
        SITE.floorCount * SITE.floorHeight - 0.2, (bMinZ + bMaxZ) / 2));

    // ---------- Giàn giáo mặt trước (bối cảnh hazard 1 & 2) ----------
    // Điểm mấu chốt: nhịp giữa CỐ TÌNH thiếu thanh giằng chéo và cả tầng trên cùng KHÔNG có lan can —
    // đúng hai lỗi mà hazard 1 ("không lan can") và hazard 2 ("thiếu giằng") mô tả. Người học xoay quanh
    // là nhìn ra được, không cần đọc chữ.
    const scaffold = new THREE.Group();
    scene.add(scaffold);
    const sx = SITE.scaffoldX;
    const bayZList = [bMinZ, bMinZ + 2, bMinZ + 4, bMaxZ + 0.5];   // 3 nhịp
    const scaffoldLevels = 4;
    const BAY_MISSING_BRACE = 1;   // nhịp giữa — nhịp bị thiếu giằng chéo
    const LEVEL_NO_GUARDRAIL = 3;  // tầng công nhân đứng làm việc, không có lan can

    bayZList.forEach(bz => {
        scaffold.add(tube(MAT.scaffold, 0.045, scaffoldLevels * SITE.floorHeight, sx, scaffoldLevels * SITE.floorHeight / 2, bz));
        scaffold.add(tube(MAT.scaffold, 0.045, scaffoldLevels * SITE.floorHeight, sx + 1.15, scaffoldLevels * SITE.floorHeight / 2, bz));
        scaffold.add(box(MAT.timber, 0.4, 0.06, 0.4, sx, 0.03, bz));            // tấm kê chân giáo
        scaffold.add(box(MAT.timber, 0.4, 0.06, 0.4, sx + 1.15, 0.03, bz));
    });

    for (let level = 1; level <= scaffoldLevels; level++) {
        const y = level * SITE.floorHeight;
        // Thanh ngang (ledger) chạy dọc mặt tiền, cả hai hàng cột
        scaffold.add(tube(MAT.scaffold, 0.035, bayZList[3] - bayZList[0], sx, y, (bayZList[0] + bayZList[3]) / 2, { axis: 'z' }));
        scaffold.add(tube(MAT.scaffold, 0.035, bayZList[3] - bayZList[0], sx + 1.15, y, (bayZList[0] + bayZList[3]) / 2, { axis: 'z' }));

        for (let bay = 0; bay < bayZList.length - 1; bay++) {
            const zMid = (bayZList[bay] + bayZList[bay + 1]) / 2;
            const zLen = bayZList[bay + 1] - bayZList[bay];
            // Thanh ngang ngang (transom) nối hai hàng cột
            scaffold.add(tube(MAT.scaffold, 0.032, 1.15, sx + 0.575, y, bayZList[bay], { axis: 'x' }));

            // Giằng chéo — trừ đúng nhịp bị lỗi
            if (bay !== BAY_MISSING_BRACE) {
                const diagLen = Math.hypot(zLen, SITE.floorHeight);
                const brace = tube(MAT.scaffold, 0.028, diagLen, sx, y - SITE.floorHeight / 2, zMid);
                brace.rotation.x = Math.atan2(zLen, SITE.floorHeight);
                scaffold.add(brace);
            }

            // Sàn công tác bằng ván gỗ
            scaffold.add(texBox(MAT.timber, 1.05, 0.05, zLen - 0.1, sx + 0.575, y + 0.05, zMid, 0, 0.9));
            // Lan can + thanh chắn chân — trừ tầng đang có công nhân làm việc (hazard 1)
            if (level !== LEVEL_NO_GUARDRAIL) {
                scaffold.add(tube(MAT.scaffold, 0.028, zLen, sx, y + 1.0, zMid, { axis: 'z' }));
                scaffold.add(tube(MAT.scaffold, 0.028, zLen, sx, y + 0.5, zMid, { axis: 'z' }));
                scaffold.add(texBox(MAT.timber, 0.04, 0.18, zLen, sx - 0.03, y + 0.14, zMid, 0, 0.9));
            }
        }
    }

    // Lưới an toàn chỉ bao được 2 nhịp đầu, nhịp còn lại trống hoác (hazard 2: "không có lưới bao che")
    const netAlpha = canvasTexture(64, 64, (ctx, w, h) => {
        ctx.fillStyle = '#000'; ctx.fillRect(0, 0, w, h);
        ctx.strokeStyle = '#fff'; ctx.lineWidth = 2;
        for (let i = 0; i <= 8; i++) {
            ctx.beginPath(); ctx.moveTo(i * w / 8, 0); ctx.lineTo(i * w / 8, h); ctx.stroke();
            ctx.beginPath(); ctx.moveTo(0, i * h / 8); ctx.lineTo(w, i * h / 8); ctx.stroke();
        }
    }, { srgb: false, repeat: [10, 26] });
    MAT.safetyNet.alphaMap = netAlpha;
    MAT.safetyNet.needsUpdate = true;
    const net = new THREE.Mesh(new THREE.PlaneGeometry(4.2, scaffoldLevels * SITE.floorHeight - 1), MAT.safetyNet);
    net.rotation.y = Math.PI / 2;
    net.position.set(sx - 0.12, (scaffoldLevels * SITE.floorHeight) / 2, bayZList[0] + 2.1);
    scene.add(net);

    // Thang leo giữa các tầng giàn giáo
    for (let level = 0; level < scaffoldLevels; level++) {
        const y = level * SITE.floorHeight;
        scaffold.add(tube(MAT.steel, 0.025, SITE.floorHeight, sx + 0.35, y + SITE.floorHeight / 2, bayZList[3] - 0.35));
        scaffold.add(tube(MAT.steel, 0.025, SITE.floorHeight, sx + 0.8, y + SITE.floorHeight / 2, bayZList[3] - 0.35));
        for (let r = 0; r < 9; r++) {
            scaffold.add(tube(MAT.steel, 0.016, 0.45, sx + 0.575, y + 0.3 + r * 0.32, bayZList[3] - 0.35, { axis: 'x' }));
        }
    }

    // ---------- Bãi vật liệu (hazard 5) ----------
    // Bó thép cây xếp cao gần 2m, KHÔNG có kê chèn, dựng sát mép lối đi — đúng mô tả tình huống.
    onProgress('Đang xếp bãi vật liệu...');
    const yard = new THREE.Group();
    scene.add(yard);
    for (let layer = 0; layer < 6; layer++) {
        const count = 6 - Math.floor(layer / 2);
        for (let i = 0; i < count; i++) {
            const bar = tube(MAT.rebar, 0.055, 4.4,
                -6.2 + (Math.random() - 0.5) * 0.25,
                0.24 + layer * 0.3,
                -4.1 + i * 0.28 + layer * 0.14 + (Math.random() - 0.5) * 0.06,
                { axis: 'x' });
            bar.rotation.y = (Math.random() - 0.5) * 0.05;
            yard.add(bar);
        }
    }
    yard.add(contactShadow(7, -6.2, -3.2, 0.5, 1.1));

    // Pallet gạch
    for (let p = 0; p < 2; p++) {
        const px = -8.6 - p * 1.6, pz = -1.4;
        yard.add(box(MAT.timber, 1.3, 0.12, 1.1, px, 0.06, pz));
        yard.add(texBox(MAT.brick, 1.15, 0.95, 0.98, px, 0.6, pz, 0, 1.2));
        yard.add(contactShadow(2.6, px, pz, 0.45));
    }
    // Chồng bao xi măng
    for (let i = 0; i < 9; i++) {
        const sxb = -9.4 + (i % 3) * 0.62, syb = 0.09 + Math.floor(i / 3) * 0.17;
        yard.add(box(MAT.plasticWhite, 0.58, 0.16, 0.9, sxb, syb, 1.8, (Math.random() - 0.5) * 0.12));
    }
    yard.add(contactShadow(3.4, -8.8, 1.8, 0.4));

    // ---------- Máy trộn bê tông + dây điện hở cạnh vũng nước (hazard 3) ----------
    const mixerGroup = new THREE.Group();
    mixerGroup.position.set(4.3, 0, -3.2);
    scene.add(mixerGroup);
    mixerGroup.add(box(MAT.steelDark, 1.5, 0.18, 1.1, 0, 0.28, 0));
    [[-0.6, 0.42], [0.6, 0.42]].forEach(([wx]) => {
        mixerGroup.add(tube(MAT.steelDark, 0.28, 0.14, wx, 0.28, 0.62, { axis: 'x', hi: true }));
    });
    const drum = new THREE.Mesh(new THREE.CylinderGeometry(0.62, 0.42, 1.15, 20), MAT.rust);
    drum.rotation.z = Math.PI / 4.4;
    drum.position.set(0, 1.0, 0);
    drum.castShadow = drum.receiveShadow = true;
    mixerGroup.add(drum);
    mixerGroup.add(tube(MAT.steelDark, 0.06, 1.3, -0.55, 0.75, 0, { rotZ: 0.5 }));
    mixerGroup.add(tube(MAT.steelDark, 0.06, 1.3, 0.55, 0.75, 0, { rotZ: -0.5 }));
    mixerGroup.add(contactShadow(3, 0, 0, 0.5));

    // Dây điện chạy từ máy trộn về phía vũng nước, có một đoạn tróc vỏ lộ lõi đồng
    const cableRun = [
        new THREE.Vector3(4.3, 0.06, -2.6), new THREE.Vector3(4.6, 0.06, -3.3),
        new THREE.Vector3(5.0, 0.05, -4.0), new THREE.Vector3(5.9, 0.06, -4.6),
        new THREE.Vector3(7.1, 0.05, -4.3), new THREE.Vector3(8.2, 0.06, -3.4),
    ];
    const cableCurve = new THREE.CatmullRomCurve3(cableRun);
    const powerCable = new THREE.Mesh(new THREE.TubeGeometry(cableCurve, 40, 0.035, 8), MAT.cableSheath);
    powerCable.castShadow = true;
    scene.add(powerCable);
    // Đoạn hở: lõi đồng sáng nhẹ (emissive) đặt ngay cạnh vũng nước — chi tiết nhỏ nhưng là toàn bộ nội
    // dung của hazard 3, phải nhìn thấy được chứ không chỉ nằm trong mô tả chữ.
    const bareWire = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.34, 8), MAT.copper);
    bareWire.rotation.z = Math.PI / 2;
    bareWire.rotation.y = 1.0;
    bareWire.position.set(5.05, 0.06, -4.05);
    scene.add(bareWire);

    // ---------- Xe tải ben đang lùi vào bãi (hazard 6) ----------
    onProgress('Đang đưa xe và thiết bị vào công trường...');
    const truckGroup = new THREE.Group();
    truckGroup.position.set(SITE.truck.x, 0, SITE.truck.z);
    truckGroup.rotation.y = THREE.MathUtils.degToRad(SITE.truck.rotY);
    scene.add(truckGroup);
    {
        const bodyMat = new THREE.MeshStandardMaterial({ color: 0x1e56c8, roughness: 0.35, metalness: 0.55 });
        const bedMat = new THREE.MeshStandardMaterial({ color: 0xb8860b, roughness: 0.6, metalness: 0.4 });
        const tyre = new THREE.MeshStandardMaterial({ color: 0x17181a, roughness: 0.95, metalness: 0 });

        truckGroup.add(box(MAT.steelDark, 1.15, 0.28, 6.0, 0, 0.62, 0));
        // Cabin (đầu xe hướng +Z, tức hướng ra ngoài công trường — xe đang LÙI vào)
        truckGroup.add(box(bodyMat, 1.85, 1.55, 1.8, 0, 1.45, 2.45));
        truckGroup.add(box(bodyMat, 1.85, 0.55, 0.7, 0, 0.72, 3.1));
        truckGroup.add(box(MAT.glass, 1.7, 0.78, 0.08, 0, 1.68, 3.34));
        [-0.94, 0.94].forEach(mx => {
            truckGroup.add(tube(MAT.steelDark, 0.02, 0.5, mx, 1.85, 3.05));
            truckGroup.add(box(MAT.steelDark, 0.06, 0.3, 0.34, mx, 1.75, 3.05));
        });
        [-0.72, 0.72].forEach(hx => {
            const head = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.16, 0.06),
                new THREE.MeshStandardMaterial({ color: 0xfff8dc, emissive: 0xfff1b0, emissiveIntensity: 0.35, roughness: 0.2 }));
            head.position.set(hx, 1.02, 3.36);
            truckGroup.add(head);
        });
        // Đèn lùi/đèn hậu ở đuôi — sáng lên vì xe đang lùi, nhưng KHÔNG có người cảnh giới phía sau
        const reverseLampMat = new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: 0xffffff, emissiveIntensity: 1.6, roughness: 0.2 });
        const tailLampMat = new THREE.MeshStandardMaterial({ color: 0xdc2626, emissive: 0xdc2626, emissiveIntensity: 1.1, roughness: 0.3 });
        [-0.8, 0.8].forEach(hx => {
            truckGroup.add(box(tailLampMat, 0.2, 0.24, 0.06, hx, 1.15, -3.06));
            truckGroup.add(box(reverseLampMat, 0.2, 0.14, 0.06, hx, 0.92, -3.06));
        });
        // Thùng ben (đuôi xe, hướng -Z — vùng điểm mù)
        const bed = box(bedMat, 2.0, 1.15, 3.6, 0, 1.42, -1.15);
        bed.rotation.x = -0.04;
        truckGroup.add(bed);
        occluderList.push(bed);
        truckGroup.add(box(MAT.steelDark, 2.02, 0.12, 3.6, 0, 0.86, -1.15));
        truckGroup.add(tube(MAT.steel, 0.07, 1.6, 0.86, 2.2, 1.55));      // ống xả đứng
        // 6 bánh: 1 trục trước + 2 trục sau (xe ben hạng nặng)
        [[-0.66, 2.05], [0.66, 2.05], [-0.66, -0.55], [0.66, -0.55], [-0.66, -1.5], [0.66, -1.5]].forEach(([wx, wz]) => {
            const wheel = tube(tyre, 0.46, 0.34, wx, 0.46, wz, { axis: 'x', hi: true });
            truckGroup.add(wheel);
            truckGroup.add(tube(MAT.steel, 0.2, 0.36, wx, 0.46, wz, { axis: 'x', hi: true }));
        });
        truckGroup.add(contactShadow(7.5, 0, -0.2, 0.5, 0.55));
    }

    // ---------- Container văn phòng công trường + kho ----------
    function siteContainer(mat, x, z, rotY, { office = false } = {}) {
        const g = new THREE.Group();
        g.position.set(x, 0, z);
        g.rotation.y = rotY;
        g.add(texBox(mat, 6.0, 2.6, 2.45, 0, 1.42, 0, 0, 1.0));
        g.add(box(MAT.steelDark, 6.1, 0.16, 2.55, 0, 2.78, 0));
        g.add(box(MAT.steelDark, 6.1, 0.16, 2.55, 0, 0.1, 0));
        if (office) {
            [-1.6, 0.2, 1.9].forEach(wx => g.add(box(MAT.glass, 0.9, 0.75, 0.06, wx, 1.7, 1.25)));
            g.add(box(MAT.steelDark, 0.85, 1.9, 0.08, -2.5, 1.1, 1.25));
            g.add(box(MAT.timber, 1.6, 0.12, 0.9, -2.5, 0.16, 1.85));
        }
        g.add(contactShadow(8, 0, 0, 0.5, 0.42));
        scene.add(g);
        occluderList.push(g);
        return g;
    }
    siteContainer(MAT.containerBlue, 15.5, 6.5, -0.35, { office: true });
    siteContainer(MAT.containerOrange, 16.5, 12.5, -0.2);

    // Nhà vệ sinh di động + máy phát điện — vặt vãnh nhưng chính những thứ này làm cảnh trông "có người ở"
    const toilet = box(MAT.plasticWhite, 1.1, 2.2, 1.1, 18.5, 1.1, 16.5);
    toilet.material = new THREE.MeshStandardMaterial({ color: 0x2563eb, roughness: 0.55 });
    scene.add(toilet, contactShadow(2.2, 18.5, 16.5, 0.45));
    scene.add(box(MAT.steel, 1.5, 0.9, 0.8, 8.5, 0.45, 8.5), contactShadow(2.4, 8.5, 8.5, 0.4));

    // ---------- Cọc tiêu, rào nhựa, biển báo ----------
    const coneMat = new THREE.MeshStandardMaterial({ color: 0xf3620c, roughness: 0.6 });
    const coneGeo = new THREE.ConeGeometry(0.24, 0.62, 14);
    const coneBaseGeo = new THREE.BoxGeometry(0.46, 0.05, 0.46);
    function cone(x, z) {
        const g = new THREE.Group();
        const c = new THREE.Mesh(coneGeo, coneMat);
        c.position.y = 0.33; c.castShadow = true;
        const b = new THREE.Mesh(coneBaseGeo, coneMat);
        b.position.y = 0.03; b.castShadow = b.receiveShadow = true;
        const band = new THREE.Mesh(new THREE.CylinderGeometry(0.155, 0.185, 0.1, 14), MAT.plasticWhite);
        band.position.y = 0.38;
        g.add(c, b, band);
        g.position.set(x, 0, z);
        scene.add(g);
    }
    // Cọc tiêu rải quanh khu vực thi công và chân cẩu — CỐ Ý không đặt dọc mép đường xe, vì hazard 8 là
    // "không hề phân làn giữa xe và người": cắm một hàng cọc dọc đường ở đây sẽ tự phủ định tình huống đó.
    [[-1.5, 7.8], [1.2, 8.6], [3.9, 7.6], [-8.4, -0.4], [-8.4, 1.2],
     [-11.2, -3.0], [-12.6, -0.6], [-16.4, -2.4], [-4.0, -4.6]].forEach(([x, z]) => cone(x, z));

    // Rào nhựa đỏ-trắng quanh khu vực cẩu (nhưng để hở đúng phía có công nhân — hazard 4)
    function barrier(x, z, rotY) {
        const g = new THREE.Group();
        g.position.set(x, 0, z); g.rotation.y = rotY;
        g.add(box(MAT.plastic, 1.9, 0.12, 0.14, 0, 0.95, 0));
        g.add(box(MAT.plasticWhite, 0.55, 0.13, 0.15, 0, 0.8, 0));
        g.add(box(MAT.plastic, 1.9, 0.12, 0.14, 0, 0.62, 0));
        g.add(box(MAT.plasticWhite, 0.12, 1.0, 0.12, -0.9, 0.5, 0));
        g.add(box(MAT.plasticWhite, 0.12, 1.0, 0.12, 0.9, 0.5, 0));
        g.add(box(MAT.plasticWhite, 0.7, 0.08, 0.6, -0.9, 0.04, 0));
        g.add(box(MAT.plasticWhite, 0.7, 0.08, 0.6, 0.9, 0.04, 0));
        scene.add(g);
    }
    [[-4.2, 9.2, 0], [-2.2, 9.2, 0], [3.2, 8.6, -0.25], [5.0, 7.8, -0.35]].forEach(([x, z, r]) => barrier(x, z, r));

    // Biển báo chữ Việt
    function sign(x, z, rotY, lines, opts) {
        const g = new THREE.Group();
        g.position.set(x, 0, z); g.rotation.y = rotY;
        g.add(tube(MAT.steel, 0.035, 1.9, 0, 0.95, 0));
        const plate = new THREE.Mesh(new THREE.PlaneGeometry(1.0, 1.0),
            new THREE.MeshStandardMaterial({ map: signTexture(lines, opts), roughness: 0.65, side: THREE.DoubleSide }));
        plate.position.set(0, 1.75, 0.03);
        plate.castShadow = true;
        g.add(plate);
        scene.add(g);
    }
    sign(-6.6, 6.4, 0.55, ['KHU VỰC', 'CẨU HOẠT ĐỘNG', 'CẤM VÀO']);
    sign(-9.2, -6.2, -0.9, ['BẮT BUỘC', 'ĐỘI MŨ', 'BẢO HỘ'], { bg: '#1d4ed8', fg: '#ffffff', accent: '#ffffff' });
    sign(17.5, -5.2, -1.4, ['CHÚ Ý', 'XE RA VÀO']);

    // Bảng nội quy an toàn ở cổng
    {
        const g = new THREE.Group();
        g.position.set(19, 0, 2);
        g.rotation.y = -Math.PI / 2.4;
        g.add(tube(MAT.steel, 0.05, 2.6, -1.3, 1.3, 0));
        g.add(tube(MAT.steel, 0.05, 2.6, 1.3, 1.3, 0));
        const boardTex = canvasTexture(512, 384, (ctx, w, h) => {
            ctx.fillStyle = '#0f2f6b'; ctx.fillRect(0, 0, w, h);
            ctx.fillStyle = '#facc15'; ctx.fillRect(0, 0, w, 62);
            ctx.fillStyle = '#0f172a'; ctx.font = 'bold 34px sans-serif'; ctx.textAlign = 'center';
            ctx.fillText('NỘI QUY AN TOÀN CÔNG TRƯỜNG', w / 2, 44);
            ctx.fillStyle = '#ffffff'; ctx.font = '24px sans-serif'; ctx.textAlign = 'left';
            ['1. Đội mũ bảo hộ 100% thời gian',
             '2. Dây an toàn khi làm việc trên 2m',
             '3. Không đi vào vùng cẩu đang hoạt động',
             '4. Xe lùi phải có người cảnh giới',
             '5. Ngắt điện trước khi sửa chữa'].forEach((t, i) => ctx.fillText(t, 34, 118 + i * 46));
        });
        const board = new THREE.Mesh(new THREE.PlaneGeometry(3.0, 2.2),
            new THREE.MeshStandardMaterial({ map: boardTex, roughness: 0.7, side: THREE.DoubleSide }));
        board.position.set(0, 1.75, 0.06);
        board.castShadow = true;
        g.add(board);
        scene.add(g);
    }

    // Cột đèn pha công trường
    [[-20, 16], [21, -17]].forEach(([x, z]) => {
        const g = new THREE.Group();
        g.position.set(x, 0, z);
        g.add(tube(MAT.steel, 0.09, 7.5, 0, 3.75, 0));
        g.add(box(MAT.steelDark, 1.3, 0.4, 0.35, 0, 7.6, 0));
        [-0.4, 0.4].forEach(ox => g.add(box(
            new THREE.MeshStandardMaterial({ color: 0xdfe7ee, roughness: 0.25, metalness: 0.4 }),
            0.5, 0.34, 0.16, ox, 7.55, 0.2)));
        scene.add(g);
    });

    // Cờ hiệu nhà thầu (có gió thổi — hình ảnh động nhỏ giúp cảnh không "chết cứng")
    const flagGeo = new THREE.PlaneGeometry(1.8, 1.1, 14, 6);
    const flagMesh = new THREE.Mesh(flagGeo, new THREE.MeshStandardMaterial({
        color: 0x1d4ed8, roughness: 0.85, side: THREE.DoubleSide,
    }));
    flagMesh.position.set(20.9, 5.4, 8);
    flagMesh.castShadow = true;
    scene.add(flagMesh, tube(MAT.steel, 0.06, 6.4, 20, 3.2, 8));
    const flagBase = flagGeo.attributes.position.array.slice();

    // ---------- Xe cút kít, xô, thang rời ----------
    {
        const g = new THREE.Group();
        g.position.set(-2.6, 0, -6.2); g.rotation.y = 0.7;
        g.add(box(MAT.steel, 0.75, 0.32, 1.0, 0, 0.5, 0));
        g.add(tube(MAT.steelDark, 0.02, 1.5, -0.28, 0.42, 0.2, { axis: 'z' }));
        g.add(tube(MAT.steelDark, 0.02, 1.5, 0.28, 0.42, 0.2, { axis: 'z' }));
        g.add(tube(MAT.steelDark, 0.18, 0.1, 0, 0.18, -0.62, { axis: 'x', hi: true }));
        scene.add(g, contactShadow(1.8, -2.6, -6.2, 0.4));
    }
    [[6.8, 5.4, 0xf59e0b], [7.4, 5.9, 0x2563eb], [-9.9, 4.2, 0xdc2626]].forEach(([x, z, c]) => {
        const bucket = new THREE.Mesh(new THREE.CylinderGeometry(0.17, 0.13, 0.32, 12),
            new THREE.MeshStandardMaterial({ color: c, roughness: 0.5 }));
        bucket.position.set(x, 0.16, z);
        bucket.castShadow = bucket.receiveShadow = true;
        scene.add(bucket);
    });

    // ---------- CẨU THÁP (model thật) ----------
    onProgress('Đang lắp dựng cẩu tháp...');
    const gltfLoader = new GLTFLoader();
    const craneGroup = new THREE.Group();
    craneGroup.position.copy(SITE.craneBase);
    // Cần trục quay sao cho cần (jib) — trục -Z cục bộ của model — chĩa đúng về phía tải treo, nhờ vậy
    // dời chân cẩu đi đâu thì hướng cần tự tính lại, không phải dò góc bằng tay.
    {
        const d = SITE.hookAnchor.clone().sub(SITE.craneBase);
        craneGroup.rotation.y = Math.atan2(-d.x, -d.z);
    }
    scene.add(craneGroup);

    let craneTopY = 18;
    try {
        const gltf = await gltfLoader.loadAsync('/models/crane.glb');
        const crane = gltf.scene;
        crane.scale.setScalar(SITE.craneScale);
        // Gốc tọa độ của model nằm lưng chừng thân cẩu chứ không ở chân — đo hộp bao rồi hạ xuống cho
        // chân cẩu chạm đất, thay vì gán một con số dịch chuyển "ma thuật" chỉ đúng với đúng file này.
        const bounds = new THREE.Box3().setFromObject(crane);
        crane.position.y -= bounds.min.y;
        craneTopY = bounds.max.y - bounds.min.y;
        crane.traverse(node => {
            if (!node.isMesh) return;
            node.castShadow = node.receiveShadow = true;
            const matList = Array.isArray(node.material) ? node.material : [node.material];
            matList.forEach(m => {
                if (m.map) m.map.anisotropy = maxAniso;
                if (m.normalMap) m.normalMap.anisotropy = maxAniso;
            });
        });
        craneGroup.add(crane);
        occluderList.push(crane);
    } catch (ex) {
        console.warn('[Mô phỏng 3D] Không tải được /models/crane.glb — cảnh vẫn chạy nhưng thiếu cẩu tháp.', ex);
        craneGroup.add(tube(MAT.scaffold, 0.4, 18, 0, 9, 0));
        craneGroup.add(box(MAT.scaffold, 0.5, 0.5, 26, 0, 18, -9));
    }
    scene.add(contactShadow(9, SITE.craneBase.x, SITE.craneBase.z, 0.55));

    // ---------- Tải treo lơ lửng dưới cần trục (hazard 7) ----------
    // Đặt trong một Group xoay quanh điểm treo trên cần: cả cáp lẫn tải cùng đung đưa như con lắc thật,
    // thay vì trượt ngang một cách giả tạo nếu dời vị trí từng vật riêng lẻ.
    const trolleyY = craneTopY - 1.1;
    const swingPivot = new THREE.Group();
    swingPivot.position.set(SITE.hookAnchor.x, trolleyY, SITE.hookAnchor.z);
    scene.add(swingPivot);
    {
        const cableLen = trolleyY - SITE.loadY;
        swingPivot.add(tube(MAT.steelDark, 0.03, cableLen, 0, -cableLen / 2, 0));
        const block = box(MAT.steelDark, 0.34, 0.4, 0.34, 0, -cableLen - 0.15, 0);
        swingPivot.add(block);
        const hook = new THREE.Mesh(new THREE.TorusGeometry(0.16, 0.045, 8, 14, Math.PI * 1.5), MAT.steel);
        hook.rotation.x = Math.PI / 2;
        hook.position.set(0, -cableLen - 0.5, 0);
        hook.castShadow = true;
        swingPivot.add(hook);
        // Tải là một bó thép cây buộc dây — kiện hàng hay gặp nhất khi cẩu tháp phục vụ thi công bê tông
        const bundle = new THREE.Group();
        bundle.position.set(0, -cableLen - 0.95, 0);
        for (let i = 0; i < 12; i++) {
            const bx = (i % 4 - 1.5) * 0.11, by = Math.floor(i / 4) * 0.11;
            bundle.add(tube(MAT.rebar, 0.05, 3.2, bx, by, 0, { axis: 'z' }));
        }
        [-1.0, 1.0].forEach(sz => bundle.add(box(MAT.steelDark, 0.55, 0.05, 0.06, 0, 0.11, sz)));
        swingPivot.add(bundle);
    }

    // ---------- CÔNG NHÂN (model thật, có animation đứng) ----------
    onProgress('Đang đưa công nhân vào vị trí...');
    // Vị trí gắn với từng điểm nguy cơ trong CSDL — mỗi người đứng đúng chỗ mà tình huống mô tả.
    const workerPlanList = [
        { pos: [-5.42, 9.08, 1.9], rotY: 92, hat: null,               note: 'hazard 1 — làm việc mép sàn giàn giáo, không lan can, không dây an toàn' },
        { pos: [0.4, 0, 6.9], rotY: 200, hat: null,                   note: 'hazard 4 — đi lại dưới bán kính cẩu, không mũ bảo hộ' },
        { pos: [1.9, 0, 7.6], rotY: 235, hat: null,                   note: 'hazard 4 — người thứ hai, cũng không mũ' },
        { pos: [9.5, 0, -3.6], rotY: 10, hat: HARD_HAT.yellow,        note: 'hazard 6 — đứng đúng điểm mù sau thùng ben xe đang lùi' },
        { pos: [-2.5, 0, 6.3], rotY: 150, hat: HARD_HAT.white,        note: 'hazard 7 — làm việc ngay dưới tải treo đang đung đưa' },
        { pos: [12.4, 0, -5.6], rotY: 285, hat: HARD_HAT.orange,      note: 'hazard 8 — cắt ngang đường xe chạy, không có làn riêng' },
        { pos: [-7.6, 0, -1.6], rotY: 60, hat: HARD_HAT.blue,         note: 'hazard 5 — bốc dỡ cạnh đống thép xếp cao' },
        { pos: [14.2, 0, 5.0], rotY: 250, hat: HARD_HAT.white,        note: 'cán bộ an toàn trước văn phòng công trường' },
    ];

    const mixerList = [];
    try {
        const gltf = await gltfLoader.loadAsync('/models/worker.glb');
        const source = gltf.scene;
        source.traverse(node => {
            if (!node.isMesh && !node.isSkinnedMesh) return;
            node.castShadow = true;
            node.receiveShadow = true;
            node.frustumCulled = false;   // skinned mesh hay bị cull nhầm vì hộp bao tính theo tư thế gốc
            const matList = Array.isArray(node.material) ? node.material : [node.material];
            matList.forEach(m => { if (m.map) m.map.anisotropy = maxAniso; });
        });
        const idleClip = gltf.animations[0] || null;

        workerPlanList.forEach((plan, index) => {
            // cloneSkeleton (SkeletonUtils) chứ không phải .clone(): nhân vật có xương, bản sao thường sẽ
            // dùng chung skeleton với bản gốc nên tất cả công nhân sẽ cử động y hệt nhau như một.
            const person = cloneSkeleton(source);
            person.position.set(plan.pos[0], plan.pos[1], plan.pos[2]);
            person.rotation.y = THREE.MathUtils.degToRad(plan.rotY);
            addSafetyGear(person, plan.hat);
            scene.add(person);
            if (plan.pos[1] < 0.5) scene.add(contactShadow(1.7, plan.pos[0], plan.pos[2], 0.5));

            if (idleClip) {
                const mixer = new THREE.AnimationMixer(person);
                const action = mixer.clipAction(idleClip);
                // Lệch pha mỗi người một chút: 8 người cùng thở/lắc y hệt nhau trông như rô-bốt ngay lập tức.
                action.time = (index * 1.37) % idleClip.duration;
                action.timeScale = 0.9 + index * 0.03;
                action.play();
                mixerList.push(mixer);
            }
        });
    } catch (ex) {
        console.warn('[Mô phỏng 3D] Không tải được /models/worker.glb — dùng hình khối thay thế.', ex);
        workerPlanList.forEach(plan => {
            const g = new THREE.Group();
            g.position.set(plan.pos[0], plan.pos[1], plan.pos[2]);
            g.add(new THREE.Mesh(new THREE.CapsuleGeometry(0.24, 0.8, 4, 10), MAT.hivis));
            g.children[0].position.y = 0.9;
            g.children[0].castShadow = true;
            scene.add(g);
        });
    }

    /** Gắn mũ bảo hộ + áo phản quang lên bộ xương nhân vật. Gắn vào XƯƠNG (không phải vào gốc nhân vật)
     *  để mũ/áo cử động theo animation — treo cố định ở một độ cao sẽ lộ ngay khi nhân vật thở/nghiêng người.
     *  `hatColor === null` nghĩa là người này KHÔNG đội mũ: đó chính là vi phạm mà bài học muốn chỉ ra. */
    function addSafetyGear(person, hatColor) {
        const chest = person.getObjectByName('mixamorig1:Spine2') || person.getObjectByName('mixamorig1:Spine1');
        if (chest) {
            const vest = new THREE.Group();
            const shell = new THREE.Mesh(new THREE.CylinderGeometry(0.185, 0.215, 0.5, 16, 1, true), MAT.hivis);
            shell.castShadow = true;
            vest.add(shell);
            [-0.1, 0.1].forEach(by => {
                const band = new THREE.Mesh(new THREE.CylinderGeometry(0.192, 0.207, 0.05, 16, 1, true), MAT.reflectiveBand);
                band.position.y = by;
                vest.add(band);
            });
            vest.position.set(0, 0.06, 0.01);
            vest.scale.setScalar(1 / (chest.getWorldScale(new THREE.Vector3()).x || 1));
            chest.add(vest);
        }
        if (!hatColor) return;
        const head = person.getObjectByName('mixamorig1:Head');
        if (!head) return;
        const hatMat = new THREE.MeshStandardMaterial({ color: hatColor, roughness: 0.32, metalness: 0.08 });
        const hat = new THREE.Group();
        const dome = new THREE.Mesh(new THREE.SphereGeometry(0.115, 18, 12, 0, Math.PI * 2, 0, Math.PI / 2), hatMat);
        const brim = new THREE.Mesh(new THREE.CylinderGeometry(0.155, 0.155, 0.016, 20), hatMat);
        const ridge = new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.04, 0.2), hatMat);
        ridge.position.y = 0.09;
        hat.add(dome, brim, ridge);
        hat.children.forEach(c => { c.castShadow = true; });
        hat.position.set(0, 0.135, 0.012);
        hat.scale.setScalar(1 / (head.getWorldScale(new THREE.Vector3()).x || 1));
        head.add(hat);
    }

    // ---------- Bụi lơ lửng ----------
    // Vài trăm hạt sáng trôi chậm trong nắng. Rẻ gần như bằng 0 nhưng là thứ khiến không khí trông "có
    // chiều sâu" — cảnh 3D ngoài trời thiếu hạt lơ lửng luôn trông sạch sẽ một cách phi thực tế.
    const dustCount = 420;
    const dustPos = new Float32Array(dustCount * 3);
    const dustSeed = new Float32Array(dustCount);
    for (let i = 0; i < dustCount; i++) {
        dustPos[i * 3] = (Math.random() - 0.5) * 46;
        dustPos[i * 3 + 1] = Math.random() * 14 + 0.4;
        dustPos[i * 3 + 2] = (Math.random() - 0.5) * 46;
        dustSeed[i] = Math.random() * Math.PI * 2;
    }
    const dustGeo = new THREE.BufferGeometry();
    dustGeo.setAttribute('position', new THREE.BufferAttribute(dustPos, 3));
    const dust = new THREE.Points(dustGeo, new THREE.PointsMaterial({
        color: 0xfff2d4, size: 0.085, transparent: true, opacity: 0.5, depthWrite: false,
        sizeAttenuation: true,
    }));
    scene.add(dust);

    // ==================== VÒNG LẶP RENDER + API CHO TRANG ====================
    const clock = new THREE.Clock();
    let running = true;
    let rafId = 0;

    // Chiếu marker điểm nguy cơ ra màn hình + hoạt cảnh lia camera: dùng chung với các bối cảnh mô phỏng
    // khác, xem js/sim-kit.js. `occluderList` được truyền theo tham chiếu nên các khối đã push ở trên đều
    // được tính vào phép kiểm tra che khuất.
    const director = createDirector({
        container, camera, controls, occluderList,
        centerX: siteCenterX, centerZ: siteCenterZ,
        focusDistance: 17,      // công trường rộng, phải lùi xa mới thấy hết một điểm nguy cơ trong ngữ cảnh
    });

    function render() {
        rafId = requestAnimationFrame(render);
        const delta = Math.min(clock.getDelta(), 0.1);
        const time = clock.elapsedTime;

        director.updateTween(delta);
        controls.update();

        for (const mixer of mixerList) mixer.update(delta);

        // Tải treo đung đưa theo gió — chính là nguy cơ mà hazard 7 nói tới, nên nó phải NHÌN THẤY được
        swingPivot.rotation.z = Math.sin(time * 0.62) * 0.055;
        swingPivot.rotation.x = Math.cos(time * 0.44) * 0.035;

        // Cờ bay: dịch chuyển đỉnh lưới theo sóng sin, biên độ tăng dần từ cột ra mép cờ
        const pos = flagGeo.attributes.position;
        for (let i = 0; i < pos.count; i++) {
            const x = flagBase[i * 3], y = flagBase[i * 3 + 1];
            const k = (x + 0.9) / 1.8;
            pos.setZ(i, Math.sin(x * 3.2 + time * 3.4) * 0.16 * k + Math.cos(y * 2 + time * 2.1) * 0.05 * k);
        }
        pos.needsUpdate = true;
        flagGeo.computeVertexNormals();

        // Bụi trôi lên chậm rồi lặp lại từ dưới
        const dp = dustGeo.attributes.position;
        for (let i = 0; i < dustCount; i++) {
            let y = dp.getY(i) + delta * (0.12 + (i % 7) * 0.02);
            if (y > 15) y = 0.3;
            dp.setY(i, y);
            dp.setX(i, dustPos[i * 3] + Math.sin(time * 0.3 + dustSeed[i]) * 0.7);
        }
        dp.needsUpdate = true;

        renderer.render(scene, camera);
        director.updateMarkers();
    }

    // Bay vào cảnh: mở bằng một cú lia rộng rồi hạ về góc nhìn làm việc — 2 giây này vừa che thời gian
    // biên dịch shader của khung hình đầu tiên (luôn giật), vừa cho người học thấy toàn cảnh công trường
    // trước khi bắt đầu đi tìm điểm nguy cơ.
    // Góc nhìn mặc định chọn từ phía -X/+Z: thấy được cùng lúc mặt tiền có giàn giáo (hazard 1, 2), tải
    // treo trên cẩu (hazard 7), nhóm công nhân dưới cẩu (hazard 4) và xa hơn là xe tải/đường xe (6, 8).
    const HOME_POSITION = new THREE.Vector3(-8, 15, 31);
    const HOME_TARGET = new THREE.Vector3(-2, 7, 0);
    camera.position.set(-16, 30, 48);
    controls.target.set(-2, 9, 0);
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

    return {
        scene, camera, renderer, controls,

        // Tự xoay / độ sáng / chụp khung hình — dùng chung với mọi bối cảnh khác, xem js/sim-kit.js.
        // 0.34 là độ phơi sáng gốc của cảnh này (đặt ở phần Renderer bên trên): mức sáng người dùng chọn
        // được nhân theo tỉ lệ với nó chứ không thay thế nó.
        ...createViewerControl({ renderer, controls, scene, camera, baseExposure: 0.34 }),

        // Đăng ký marker, lia camera tới một điểm nguy cơ, quay về góc nhìn tổng thể — xem js/sim-kit.js.
        addMarker: director.addMarker,
        focus: director.focus,
        resetCamera: director.resetCamera,

        /** Tạm dừng hẳn vòng lặp khi người dùng chuyển sang tab khác (VD khung xem Sketchfab): GPU rảnh
         *  hoàn toàn thay vì render một cảnh không ai nhìn. */
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
