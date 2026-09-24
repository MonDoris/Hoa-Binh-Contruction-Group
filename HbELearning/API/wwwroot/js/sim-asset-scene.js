// File: wwwroot/js/sim-asset-scene.js
// ============================================================================================
// BỐI CẢNH DO NGƯỜI SOẠN BÀI TỰ TẢI LÊN — MỘT FILE DỰNG CẢNH DÙNG CHUNG CHO MỌI KỊCH BẢN
//
// Khác với sim-scene.js / sim-hr-scene.js (mỗi file dựng tay đúng MỘT không gian bằng hàng nghìn dòng
// code), file này không biết trước cảnh trông thế nào: nó nhận đường dẫn tệp mà nghiệp vụ vừa tải lên
// rồi dựng cảnh từ chính tệp đó. Nhờ vậy thêm một bối cảnh mới KHÔNG còn cần lập trình viên — đây là
// toàn bộ lý do file này tồn tại.
//
// Bốn loại tệp (khớp ScenarioScene.AssetType, server kiểm trong ScenarioController.AssetTypeList):
//   glb         model 3D .glb/.gltf — xoay ngắm quanh vật thể (tải trên Sketchfab, quét bằng Polycam...)
//   pano-image  ảnh 360° equirectangular — đứng giữa nhìn quanh, hợp ảnh chụp bằng điện thoại tại hiện trường
//   pano-video  video 360°
//   image-2d    ảnh thường — xem phẳng, kéo và phóng to, hợp sơ đồ mặt bằng / ảnh chụp thiết bị
//
// HỆ TỌA ĐỘ PHẢI GIỐNG HỆT NHAU giữa lúc soạn bài và lúc học, nếu không tình huống sẽ nằm lệch chỗ so
// với nơi chính người soạn đã bấm. Vì vậy mọi phép chuẩn hóa (thu model về hộp bao MODEL_SIZE, đặt đáy
// model tại y = 0, bán kính quả cầu ảnh 360°) đều nằm ở ĐÂY, và cả hai màn hình đều gọi chung hàm này:
// trang học (pages/simulation.html) gọi để dựng cảnh, trang soạn bài (pages/course-detail.html) gọi
// cùng hàm đó và truyền thêm `onPick` để nhận tọa độ mỗi khi người dùng bấm chuột lên cảnh.
// ============================================================================================

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { createDirector, createViewerControl } from './sim-kit.js';
import { createCharacterLayer } from './sim-characters.js';

/** Mọi model đều được thu/phóng về đúng hộp bao này — xem phần chuẩn hóa bên dưới. */
export const MODEL_SIZE = 10;

/** Bán kính quả cầu ảnh 360°. Chỉ HƯỚNG của điểm là có ý nghĩa nên đổi số này cũng không làm hỏng dữ
 *  liệu cũ — toScenePoint() luôn chiếu lại tọa độ đã lưu về đúng mặt cầu hiện hành. */
export const PANO_RADIUS = 50;

/** Độ lệch của camera so với tâm quả cầu (OrbitControls không cho camera trùng điểm ngắm). */
const PANO_EYE = 0.12;

const ADDON_CDN = 'https://unpkg.com/three@0.160.0/examples/jsm/libs/';

/**
 * @param {HTMLElement} container phần tử bọc canvas (marker được đặt tuyệt đối bên trong)
 * @param {(text: string) => void} onProgress báo tiến trình ra màn hình chờ
 * @param {object} option
 * @param {string} option.assetUrl đường dẫn tệp bối cảnh (/uploads/scenario/... hoặc /models/...)
 * @param {string} option.assetType glb | pano-image | pano-video | image-2d
 * @param {(point: {x:number,y:number,z:number}|null) => void} [option.onPick] bật chế độ SOẠN BÀI: bấm
 *        vào cảnh thì trả về tọa độ điểm vừa bấm, hoặc null nếu bấm trượt ra khoảng không. Không truyền =
 *        chế độ học, bấm chuột chỉ để xoay cảnh.
 * @param {(id:number, state:object) => void} [option.onCharacterDrag] đang KÉO một nhân vật — gọi liên tục
 *        theo từng khung hình với vị trí (và lối đi) mới. Chỉ để cập nhật giao diện, đừng gọi API ở đây.
 * @param {(id:number, state:object) => void} [option.onCharacterDrop] vừa BUÔNG TAY sau một cú kéo — đây là
 *        chỗ lưu lại vị trí mới.
 * @param {(id:number) => void} [option.onCharacterClick] bấm vào một nhân vật mà không kéo đi đâu.
 * @param {Array} [option.characterList] những người được đặt trong cảnh (ScenarioCharacterDto). Chỉ có tác
 *        dụng với bối cảnh model 3D — ảnh/video 360° và ảnh phẳng không có mặt sàn để người đứng lên.
 * @param {number} [option.characterScale] cỡ người của cả cảnh (ScenarioScene.CharacterScale).
 * @param {boolean} [option.transparent] không vẽ nền của cảnh, để lộ nền của phần tử bọc phía sau. Dùng
 *        cho khung soạn bài nhúng trong hộp thoại — ở đó một mảng nền xanh xám đặc chỉ làm cảnh trông như
 *        bị dán đè lên hộp thoại. Trang học vẫn để mặc định (có nền) vì cảnh chiếm cả màn hình.
 */
export async function createAssetWorld(container, onProgress = () => {}, option = {}) {
    const assetUrl = option.assetUrl;
    const assetType = option.assetType || 'glb';
    const onPick = option.onPick || null;
    const onCharacterDrag = option.onCharacterDrag || null;
    const onCharacterDrop = option.onCharacterDrop || null;
    const onCharacterClick = option.onCharacterClick || null;
    const transparent = !!option.transparent;
    if (!assetUrl) throw new Error('Mô phỏng này chưa được gán tệp bối cảnh.');

    const isPano = assetType === 'pano-image' || assetType === 'pano-video';
    const isFlat = assetType === 'image-2d';
    const isModel = !isPano && !isFlat;

    const viewWidth = () => container.clientWidth || 640;
    const viewHeight = () => container.clientHeight || 420;

    // ---------- Renderer ----------
    // alpha chỉ bật được lúc TẠO renderer (không đổi được sau) nên phải quyết định ngay từ đây.
    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: transparent, powerPreference: 'high-performance' });
    if (transparent) renderer.setClearColor(0x000000, 0);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.setSize(viewWidth(), viewHeight());
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    // Ảnh và video là thứ người soạn bài ĐÃ NHÌN THẤY trước khi tải lên: mọi phép nén dải sáng (tone
    // mapping) đều làm nó khác đi so với bản gốc và họ sẽ tưởng hệ thống làm hỏng ảnh. Chỉ bật cho model
    // 3D — nơi ánh sáng do chính ta dựng nên mới cần nén dải sáng.
    if (isModel) {
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 1.0;
        renderer.shadowMap.enabled = true;
        renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    }
    container.insertBefore(renderer.domElement, container.firstChild);

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(isPano ? 72 : (isFlat ? 42 : 50),
        viewWidth() / viewHeight(), 0.05, 400);
    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.07;

    const pickTargetList = [];   // mesh nhận tia bấm chuột khi soạn bài
    const occluderList = [];     // khối lớn dùng để làm mờ marker bị che (xem createDirector)
    const cleanupList = [];      // việc dọn dẹp riêng của từng loại tệp
    let homePosition = new THREE.Vector3(0, 0, 10);
    let homeTarget = new THREE.Vector3(0, 0, 0);
    let mixer = null;            // model có sẵn hoạt cảnh thì cho chạy
    let fitFlatDistance = null;  // hàm tính lại khoảng cách vừa khung cho ảnh phẳng
    let meshCount = 0;           // thống kê hiện trên thanh công cụ (chỉ có nghĩa với model 3D)
    let triangleCount = 0;

    // ==================== DỰNG CẢNH THEO LOẠI TỆP ====================
    if (isModel) {
        const { root, animationList } = await loadModel(assetUrl, renderer, onProgress);

        // CHUẨN HÓA — bước quan trọng nhất của cả file. Model tải trên mạng về có đủ mọi đơn vị (mm, inch,
        // "1 đơn vị = 1 tòa nhà") và đủ mọi gốc tọa độ. Không chuẩn hóa thì cùng một đoạn code đặt camera
        // khi thì lọt vào trong lòng vật thể, khi thì đứng cách xa hàng km — và tọa độ tình huống lưu
        // trong CSDL cũng không còn ý nghĩa gì để đọc. Sau bước này, MỌI model đều nằm gọn trong hộp bao
        // 10 đơn vị, tâm ở gốc tọa độ, đáy đặt trên mặt phẳng y = 0.
        const box = new THREE.Box3().setFromObject(root);
        const span = box.getSize(new THREE.Vector3());
        root.scale.setScalar(MODEL_SIZE / Math.max(span.x, span.y, span.z, 1e-4));
        root.updateMatrixWorld(true);
        box.setFromObject(root);
        const center = box.getCenter(new THREE.Vector3());
        root.position.sub(new THREE.Vector3(center.x, box.min.y, center.z));
        root.updateMatrixWorld(true);
        box.setFromObject(root);

        root.traverse(obj => {
            if (!obj.isMesh) return;
            obj.castShadow = true;
            obj.receiveShadow = true;
            obj.frustumCulled = false;
        });
        scene.add(root);
        pickTargetList.push(root);
        occluderList.push(root);

        // Ánh sáng: env map sinh từ RoomEnvironment (một phòng studio dựng sẵn của Three.js) để bề mặt kim
        // loại/bóng nào cũng có thứ gì đó mà phản chiếu — thiếu nó thì vật liệu metalness cao ra màu đen
        // đặc, và đó chính là lỗi "model tải lên bị đen sì" hay gặp nhất.
        const pmrem = new THREE.PMREMGenerator(renderer);
        const envTarget = pmrem.fromScene(new RoomEnvironment(), 0.04);
        scene.environment = envTarget.texture;
        pmrem.dispose();
        cleanupList.push(() => envTarget.dispose());

        const sun = new THREE.DirectionalLight(0xfff4e2, 2.6);
        sun.position.set(MODEL_SIZE * 0.7, MODEL_SIZE * 1.3, MODEL_SIZE * 0.9);
        sun.castShadow = true;
        sun.shadow.mapSize.set(2048, 2048);
        // Khung chiếu bóng bó đúng hộp bao đã chuẩn hóa: vì mọi model đều được thu về cùng kích thước nên
        // một bộ số duy nhất dùng chung được cho mọi tệp khách tải lên.
        sun.shadow.camera.left = -MODEL_SIZE; sun.shadow.camera.right = MODEL_SIZE;
        sun.shadow.camera.top = MODEL_SIZE; sun.shadow.camera.bottom = -MODEL_SIZE;
        sun.shadow.camera.near = 0.5; sun.shadow.camera.far = MODEL_SIZE * 4;
        sun.shadow.bias = -0.0006;
        sun.shadow.normalBias = 0.02;
        scene.add(sun, new THREE.HemisphereLight(0xdfe9f5, 0x8a8577, 0.85));

        // Mặt phẳng CHỈ hứng bóng (ShadowMaterial), không có màu riêng: model trông như đang đứng trên nền
        // thay vì lơ lửng, mà ta không áp một màu sàn bịa ra lên bối cảnh của khách.
        const ground = new THREE.Mesh(new THREE.PlaneGeometry(MODEL_SIZE * 12, MODEL_SIZE * 12),
            new THREE.ShadowMaterial({ opacity: 0.26 }));
        ground.rotation.x = -Math.PI / 2;
        ground.receiveShadow = true;
        scene.add(ground);

        const grid = new THREE.GridHelper(MODEL_SIZE * 4, 32, 0x94a3b8, 0xc7d2de);
        grid.material.transparent = true;
        grid.material.opacity = 0.4;
        scene.add(grid);
        if (!transparent) scene.background = gradientTexture('#e9eff7', '#b6c5d7');

        const radius = box.getBoundingSphere(new THREE.Sphere()).radius;
        homePosition = new THREE.Vector3(radius * 1.35, radius * 1.15, radius * 1.95);
        homeTarget = new THREE.Vector3(0, MODEL_SIZE * 0.42, 0);
        controls.maxPolarAngle = Math.PI * 0.495;   // chặn không cho chui xuống dưới nền
        controls.minDistance = MODEL_SIZE * 0.2;
        controls.maxDistance = MODEL_SIZE * 6;

        if (animationList.length) {
            mixer = new THREE.AnimationMixer(root);
            for (const clip of animationList) mixer.clipAction(clip).play();
        }

        // Số liệu thống kê hiện trên thanh công cụ trình xem: model quét bằng điện thoại thường vài triệu
        // tam giác và làm máy cấu hình yếu của học viên đứng hình — người soạn bài phải thấy con số này
        // TRƯỚC khi gán model vào bài học, chứ không phải nghe học viên phàn nàn rồi mới biết.
        root.traverse(obj => {
            if (!obj.isMesh || !obj.geometry) return;
            meshCount++;
            const index = obj.geometry.index;
            const position = obj.geometry.attributes.position;
            triangleCount += index ? index.count / 3 : (position ? position.count / 3 : 0);
        });

    } else if (isPano) {
        onProgress(assetType === 'pano-video' ? 'Đang tải video 360°...' : 'Đang tải ảnh 360°...');
        const loaded = assetType === 'pano-video'
            ? await loadVideoTexture(assetUrl)
            : await loadImageTexture(assetUrl);
        cleanupList.push(loaded.cleanup);

        // Quả cầu lộn mặt trong: scale(-1,1,1) lật pháp tuyến vào trong để camera đứng giữa nhìn thấy ảnh
        // (cách dựng ảnh 360° chuẩn của Three.js). Phép lật này cũng đảo chiều tam giác nên tia bấm chuột
        // từ bên trong vẫn trúng mặt trước — nhờ đó soạn bài mới bấm chọn vị trí được.
        const geometry = new THREE.SphereGeometry(PANO_RADIUS, 64, 40);
        geometry.scale(-1, 1, 1);
        const sphere = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({ map: loaded.texture }));
        scene.add(sphere);
        pickTargetList.push(sphere);

        // Camera đứng gần như đúng tâm quả cầu. OrbitControls đòi camera phải cách điểm ngắm một đoạn khác
        // 0 nên đặt lệch 0.12 đơn vị — so với bán kính 50 thì thị sai bằng không, người xem vẫn thấy đúng
        // một ảnh 360°. rotateSpeed ÂM vì đang nhìn từ trong ra: kéo chuột sang phải phải thấy cảnh trôi
        // sang phải giống như quay đầu, để dấu dương thì cảnh chạy ngược lại và rất chóng mặt.
        controls.rotateSpeed = -0.32;
        controls.enablePan = false;
        controls.enableZoom = false;   // thu phóng bằng góc mở ống kính bên dưới, không dời camera
        controls.minDistance = controls.maxDistance = PANO_EYE;
        homePosition = new THREE.Vector3(0, 0, PANO_EYE);
        homeTarget = new THREE.Vector3(0, 0, 0);

        const onWheel = (ev) => {
            ev.preventDefault();
            camera.fov = THREE.MathUtils.clamp(camera.fov + ev.deltaY * 0.05, 30, 92);
            camera.updateProjectionMatrix();
        };
        renderer.domElement.addEventListener('wheel', onWheel, { passive: false });
        cleanupList.push(() => renderer.domElement.removeEventListener('wheel', onWheel));

        // Vài trình duyệt vẫn chặn tự phát dù video đã tắt tiếng — phát lại ở cú chạm đầu tiên.
        if (loaded.video) {
            const resume = () => loaded.video.play().catch(() => {});
            renderer.domElement.addEventListener('pointerdown', resume, { once: true });
        }

    } else {
        onProgress('Đang tải ảnh bối cảnh...');
        const loaded = await loadImageTexture(assetUrl);
        cleanupList.push(loaded.cleanup);

        const image = loaded.texture.image;
        const aspect = (image.width || 16) / (image.height || 9);
        const planeWidth = MODEL_SIZE;
        const planeHeight = MODEL_SIZE / aspect;
        const plane = new THREE.Mesh(new THREE.PlaneGeometry(planeWidth, planeHeight),
            new THREE.MeshBasicMaterial({ map: loaded.texture }));
        scene.add(plane);
        pickTargetList.push(plane);
        if (!transparent) scene.background = new THREE.Color(0x1e293b);   // nền tối để thấy rõ mép ảnh

        // Ảnh phẳng thì XOAY chỉ làm mất phương hướng (nhìn nghiêng một tấm ảnh chẳng để làm gì) — chỉ cho
        // kéo và phóng to, đúng thao tác người dùng chờ đợi ở một trình xem ảnh.
        controls.enableRotate = false;
        controls.screenSpacePanning = true;

        // Khoảng cách vừa khung phụ thuộc tỉ lệ khung hình nên phải tính lại mỗi lần đổi kích thước.
        fitFlatDistance = () => {
            const halfFov = THREE.MathUtils.degToRad(camera.fov) / 2;
            const forHeight = (planeHeight / 2) / Math.tan(halfFov);
            const forWidth = (planeWidth / 2) / (Math.tan(halfFov) * camera.aspect);
            return Math.max(forHeight, forWidth) * 1.06;
        };
        const distance = fitFlatDistance();
        homePosition = new THREE.Vector3(0, 0, distance);
        homeTarget = new THREE.Vector3(0, 0, 0);
        controls.minDistance = distance * 0.12;
        controls.maxDistance = distance * 1.5;
    }

    // ==================== MARKER, CAMERA, VÒNG LẶP ====================
    const director = createDirector({
        container, camera, controls, occluderList,
        centerX: 0, centerZ: 0,
        focusDistance: MODEL_SIZE * 0.95,
        // Marker trên ảnh 360° luôn cách camera đúng bằng bán kính cầu nên lấy chính bán kính đó làm mẫu
        // số: mọi marker hiện cùng một cỡ, đúng như các điểm đánh dấu dán trên một tấm ảnh.
        markerScaleBase: isPano ? PANO_RADIUS * 0.92 : MODEL_SIZE * 1.7,
    });

    /** Đưa tọa độ lưu trong CSDL về đúng điểm trong cảnh hiện tại. Với ảnh 360° chỉ HƯỚNG là có ý nghĩa,
     *  nên tọa độ được chiếu lại lên mặt cầu — dữ liệu cũ vẫn đúng kể cả khi đổi PANO_RADIUS. */
    function toScenePoint(x, y, z) {
        const point = new THREE.Vector3(x, y, z);
        if (!isPano) return point;
        if (point.lengthSq() < 1e-6) point.set(0, 0, -1);
        return point.normalize().multiplyScalar(PANO_RADIUS * 0.97);
    }

    function focus(x, y, z) {
        if (isPano) {
            // Camera không đi đâu được (đang ở tâm quả cầu) nên "lia tới" ở đây nghĩa là QUAY ĐẦU nhìn về
            // hướng đó: đặt camera ở phía đối diện tâm, điểm ngắm giữ nguyên ở tâm.
            const direction = new THREE.Vector3(x, y, z);
            if (direction.lengthSq() < 1e-6) direction.set(0, 0, -1);
            direction.normalize();
            director.tweenCamera(direction.multiplyScalar(-PANO_EYE), new THREE.Vector3(0, 0, 0), 0.9);
            return;
        }
        if (isFlat) {
            // Ảnh phẳng: tiến thẳng vào điểm vừa bấm, giữ hướng nhìn vuông góc với mặt ảnh.
            const near = Math.max(camera.position.z * 0.55, homePosition.z * 0.3);
            director.tweenCamera(new THREE.Vector3(x, y, near), new THREE.Vector3(x, y, 0), 0.9);
            return;
        }
        director.focus(x, y, z);
    }

    // ==================== NHÂN VẬT ====================
    // Người được đặt vào cảnh không nằm trong tệp bối cảnh (tệp đó là phòng ốc trống) mà đến từ bảng
    // ScenarioCharacter, nên dựng thành một lớp riêng chồng lên trên. Ảnh 360°/video 360°/ảnh phẳng không
    // nhận nhân vật: chúng không có mặt sàn để người đứng lên, đặt vào chỉ ra một hình người lơ lửng.
    const characterLayer = isModel ? createCharacterLayer(scene, { sceneScale: option.characterScale }) : null;

    // Dấu tình huống nào đang bám theo người nào — cập nhật mỗi khung hình để dấu đi theo người đang đi.
    const markerFollowMap = new Map();
    const followPoint = new THREE.Vector3();

    if (characterLayer) {
        for (const character of option.characterList || []) {
            // Không await: một cảnh có hai chục người thì chờ lần lượt từng người là màn hình chờ kéo dài
            // thêm vài giây, trong khi cảnh đã sẵn sàng xem được rồi. Người xuất hiện dần cũng không sao.
            characterLayer.add(character).catch(ex =>
                console.warn('[Nhân vật 3D] Không dựng được nhân vật', character.id, ex));
        }
    }

    // Bấm để lấy tọa độ và KÉO RÊ nhân vật (chỉ ở màn soạn bài).
    //
    // Ba thao tác cùng chen nhau trên một nút chuột trái: xoay cảnh, bấm chọn một vị trí, và kéo một người
    // đi chỗ khác. Phân biệt bằng thứ nằm DƯỚI con trỏ lúc bấm xuống — trúng một nhân vật thì cả cú bấm đó
    // thuộc về nhân vật ấy, còn lại thuộc về cảnh. Quyết ngay từ lúc bấm xuống chứ không đợi tới lúc nhả:
    // OrbitControls phải bị tắt NGAY, nếu không kéo người mà cảnh xoay theo thì không tài nào thả đúng chỗ.
    if (onPick) {
        const canvasEl = renderer.domElement;
        canvasEl.style.cursor = 'crosshair';
        const raycaster = new THREE.Raycaster();
        const pointer = new THREE.Vector2();
        let downX = 0, downY = 0, downAt = 0;

        /** Ngưỡng rung tay (px): dưới ngưỡng này coi là BẤM, trên là KÉO. */
        const DRAG_THRESHOLD = 5;

        let dragId = null;       // nhân vật đang cầm; null = cú bấm này thuộc về cảnh
        let dragMoved = false;   // đã vượt ngưỡng rung tay chưa
        let dragState = null;    // trạng thái mới nhất của người đang kéo
        let lastHoverAt = 0;     // lần đổi hình con trỏ gần nhất (xem phần rê chuột bên dưới)

        // Mặt phẳng ngang ngang chân người đang kéo. Dùng khi con trỏ ra khỏi phần có bề mặt của cảnh — khe
        // giữa hai món đồ, hay kéo hẳn ra ngoài trời. Thiếu nó thì người khựng lại giữa chừng trong khi con
        // trỏ vẫn chạy tiếp, và người dùng tưởng mình đã lỡ tay buông ra.
        const dragPlane = new THREE.Plane();
        const dragPoint = new THREE.Vector3();
        const UP = new THREE.Vector3(0, 1, 0);

        // Khoảng lệch NGANG giữa chân người và điểm con trỏ chiếu xuống sàn, đo đúng lúc bắt đầu cầm.
        //
        // Không có nó thì vừa bấm vào ngực một người là người đó nhảy phắt tới chỗ tia bấm chạm sàn — mà tia
        // xuyên qua ngực thì chạm sàn ở tận phía sau lưng họ. Giữ nguyên khoảng lệch này suốt cú kéo thì
        // người đi theo tay đúng như mình đang cầm, không giật một cái ngay từ đầu.
        //
        // Chỉ giữ phần NGANG: cao độ vẫn lấy theo bề mặt dưới con trỏ, để kéo người lên bục hay xuống bậc
        // thang thì chân họ bám đúng mặt sàn ở đó chứ không lơ lửng theo chỗ cũ.
        const dragOffset = new THREE.Vector3();
        const dragFrom = new THREE.Vector3();
        const surfaceNormal = new THREE.Vector3();
        const normalMatrix = new THREE.Matrix3();

        /** Chĩa tia theo con trỏ. */
        const aimAt = (ev) => {
            const rect = canvasEl.getBoundingClientRect();
            pointer.set(((ev.clientX - rect.left) / rect.width) * 2 - 1,
                        -((ev.clientY - rect.top) / rect.height) * 2 + 1);
            raycaster.setFromCamera(pointer, camera);
        };

        /**
         * Điểm ĐẶT CHÂN ĐƯỢC dưới con trỏ: bề mặt gần nhất mà mặt trên hướng lên trời — sàn, mặt bàn, bục,
         * bậc thang. Không có thì rơi về một mặt phẳng ngang ngang chân người.
         *
         * Phải lọc theo hướng bề mặt chứ không lấy bừa vật thể đầu tiên tia chạm phải. Tia đi từ mắt xuyên
         * qua NGƯỜI rồi mới chạm cảnh, nên thứ nó chạm rất hay là bức tường phía sau lưng họ: lấy luôn điểm
         * đó thì người trèo ngược lên tường, và vì tường ở xa nên nhích chuột một li là người bay đi mấy
         * mét. Mặt sàn thì luôn ở ngay dưới chân và gần như vuông góc với hướng nhìn, kéo mới đầm tay.
         */
        const scenePointAt = (ev, fallbackY) => {
            aimAt(ev);
            for (const hit of raycaster.intersectObjects(pickTargetList, true)) {
                if (!hit.face) continue;
                surfaceNormal.copy(hit.face.normal)
                    .applyMatrix3(normalMatrix.getNormalMatrix(hit.object.matrixWorld)).normalize();
                // 0.5 ~ dốc 60°: sàn, mặt bàn, bậc thang thì đạt; tường, hông tủ, mặt nghiêng dựng đứng thì không.
                if (surfaceNormal.y > 0.5) return hit.point;
            }
            dragPlane.setFromNormalAndCoplanarPoint(UP, dragPoint.set(0, fallbackY, 0));
            return raycaster.ray.intersectPlane(dragPlane, dragPoint);
        };

        /** @param {boolean} [cancelled] con trỏ bị mất giữa chừng, KHÔNG phải người dùng buông tay. */
        const stopDrag = (cancelled = false) => {
            const id = dragId, moved = dragMoved, state = dragState;
            dragId = null; dragMoved = false; dragState = null;
            if (id == null) return false;

            controls.enabled = true;
            canvasEl.style.cursor = 'crosshair';
            // Không tự nhả pointer capture: trình duyệt tự nhả khi chuột nhấc lên, và OrbitControls cũng nhả
            // một lần nữa ngay sau đây — nhả trước nó là mở đường cho một lỗi ném ra giữa chừng, làm
            // OrbitControls kẹt lại ở trạng thái đang xoay và cảnh tự quay theo con trỏ dù không ai bấm.
            characterLayer?.setDragging(id, false);

            // Mất con trỏ giữa chừng thì đây không phải một quyết định của người dùng: người vẫn nằm ở
            // chỗ đang kéo dở, nhưng không lưu và cũng không coi là một cú bấm chọn.
            if (cancelled) return true;

            // Kéo đi thật thì lưu vị trí mới; chỉ bấm mà không kéo thì coi như chọn người đó để chỉnh.
            if (moved && state) onCharacterDrop?.(id, state);
            else onCharacterClick?.(id);
            return true;
        };

        const onPointerDown = (ev) => {
            downX = ev.clientX; downY = ev.clientY; downAt = performance.now();
            dragId = null; dragMoved = false; dragState = null;
            // Chỉ nút trái mới cầm được người: nút phải/giữa là để OrbitControls kéo tịnh tiến cảnh.
            // Không có onCharacterDrop = màn hình gọi tới chỉ XEM (bài đã duyệt, người không có quyền sửa):
            // ở đó kéo được người sẽ là lời hứa suông, vì buông tay ra chẳng có chỗ nào lưu lại.
            if (!characterLayer || !onCharacterDrop || ev.button !== 0) return;

            aimAt(ev);
            dragId = characterLayer.pickCharacter(raycaster);
            if (dragId == null) return;

            dragOffset.set(0, 0, 0);
            if (characterLayer.getPosition(dragId, dragFrom)) {
                const start = scenePointAt(ev, dragFrom.y);
                if (start) dragOffset.set(dragFrom.x - start.x, 0, dragFrom.z - start.z);
            }

            controls.enabled = false;
            canvasEl.setPointerCapture?.(ev.pointerId);
            canvasEl.style.cursor = 'grabbing';
        };

        const onPointerMove = (ev) => {
            if (dragId == null) {
                // Con trỏ đổi thành bàn tay khi rê qua người — đây là thứ DUY NHẤT cho biết người trong
                // cảnh kéo được. Chỉ dò lúc không giữ chuột và cách nhau vài chục mili giây: mỗi lần dò là
                // một lượt bắn tia qua toàn bộ lưới nhân vật, làm mỗi lần rê chuột thì cảnh sẽ giật.
                if (!characterLayer || !onCharacterDrop || ev.buttons !== 0) return;
                const now = performance.now();
                if (now - lastHoverAt < 80) return;
                lastHoverAt = now;
                aimAt(ev);
                canvasEl.style.cursor = characterLayer.pickCharacter(raycaster) != null ? 'grab' : 'crosshair';
                return;
            }

            if (!dragMoved) {
                if (Math.hypot(ev.clientX - downX, ev.clientY - downY) <= DRAG_THRESHOLD) return;
                dragMoved = true;
                characterLayer.setDragging(dragId, true);
            }

            const point = scenePointAt(ev, characterLayer.getPosition(dragId, dragFrom)?.y ?? 0);
            if (!point) return;
            dragState = characterLayer.dragTo(dragId,
                point.x + dragOffset.x, point.y, point.z + dragOffset.z);
            if (dragState) onCharacterDrag?.(dragId, dragState);
        };

        const onPointerUp = (ev) => {
            // Cú bấm dính vào một nhân vật đã được xử lý trọn ở stopDrag — KHÔNG được rơi tiếp xuống phần
            // chọn vị trí bên dưới, nếu không kéo người xong lại đặt luôn người đang sửa vào chỗ vừa thả.
            if (stopDrag()) return;

            if (Math.hypot(ev.clientX - downX, ev.clientY - downY) > DRAG_THRESHOLD) return;  // đang kéo xoay
            if (performance.now() - downAt > 700) return;                          // giữ lâu = thao tác khác
            aimAt(ev);
            const hit = raycaster.intersectObjects(pickTargetList, true)[0];
            // Bấm trượt ra khoảng không (rất hay xảy ra với model mảnh như cần cẩu, giàn giáo) thì vẫn phải
            // BÁO cho màn soạn bài biết. Im lặng bỏ qua là cách nhanh nhất khiến người dùng tưởng chức năng
            // hỏng: họ bấm mấy lần không thấy gì rồi bỏ cuộc, trong khi chỉ cần bấm trúng vào bề mặt.
            if (!hit) { onPick(null); return; }
            const round = (v) => Math.round(v * 1000) / 1000;
            onPick({ x: round(hit.point.x), y: round(hit.point.y), z: round(hit.point.z) });
        };

        canvasEl.addEventListener('pointerdown', onPointerDown);
        canvasEl.addEventListener('pointermove', onPointerMove);
        canvasEl.addEventListener('pointerup', onPointerUp);
        // Mất con trỏ giữa chừng (chuyển cửa sổ, tay rời màn cảm ứng) mà không trả OrbitControls về thì cả
        // cảnh cứng đờ không xoay được nữa, và người thì dính luôn ở chỗ dở dang.
        const onPointerCancel = () => stopDrag(true);
        canvasEl.addEventListener('pointercancel', onPointerCancel);
        cleanupList.push(() => {
            canvasEl.removeEventListener('pointerdown', onPointerDown);
            canvasEl.removeEventListener('pointermove', onPointerMove);
            canvasEl.removeEventListener('pointerup', onPointerUp);
            canvasEl.removeEventListener('pointercancel', onPointerCancel);
        });
    }

    const clock = new THREE.Clock();
    let running = true;
    let rafId = 0;

    function render() {
        rafId = requestAnimationFrame(render);
        const delta = Math.min(clock.getDelta(), 0.1);
        director.updateTween(delta);
        controls.update();
        if (mixer) mixer.update(delta);
        if (characterLayer) {
            characterLayer.tick(delta);
            // Dấu tình huống gắn vào người phải chạy theo người — nhân vật đang đi mà dấu đứng yên thì
            // người học bấm vào dấu lại thấy chỗ đó không có ai.
            markerFollowMap.forEach((characterId, markerId) => {
                if (characterLayer.getWorldPosition(characterId, followPoint))
                    director.moveMarker(markerId, followPoint.x, followPoint.y, followPoint.z);
            });
        }
        renderer.render(scene, camera);
        director.updateMarkers();
    }

    camera.position.copy(homePosition);
    controls.target.copy(homeTarget);
    controls.update();
    director.setHome(homePosition, homeTarget);
    render();

    // Model 3D: mở màn bằng một cú lia từ xa vào — vừa che khung hình đầu tiên (luôn giật vì trình duyệt
    // đang biên dịch shader), vừa cho người xem thấy toàn cảnh trước khi bắt đầu.
    if (isModel) {
        camera.position.copy(homePosition).multiplyScalar(1.7);
        director.tweenCamera(homePosition, homeTarget, 1.6);
    }

    const resizeObserver = new ResizeObserver(() => {
        const w = container.clientWidth, h = container.clientHeight;
        if (!w || !h) return;
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
        renderer.setSize(w, h);
        // Ảnh phẳng: khung hẹp lại thì khoảng cách "vừa khung" cũng khác — cập nhật để nút Góc nhìn tổng
        // thể luôn đưa về đúng khung ảnh đầy đủ.
        if (fitFlatDistance) {
            homePosition.setZ(fitFlatDistance());
            director.setHome(homePosition, homeTarget);
        }
    });
    resizeObserver.observe(container);

    return {
        scene, camera, renderer, controls, assetType,

        /** Đăng ký marker cho một tình huống lấy từ CSDL.
         *
         *  `characterId` khác null nghĩa là tình huống này gắn vào một nhân vật: marker sẽ tự bám theo người
         *  đó thay vì đứng ở tọa độ cố định. Nhân vật không dựng được (thiếu tệp, dữ liệu hỏng) thì marker
         *  vẫn nằm ở tọa độ đã lưu — tình huống không bao giờ biến mất khỏi bài học chỉ vì thiếu hình người. */
        addMarker(id, x, y, z, el, characterId = null) {
            const point = toScenePoint(x, y, z);
            director.addMarker(id, point.x, point.y, point.z, el);
            if (characterId != null && characterLayer) markerFollowMap.set(id, characterId);
        },

        /** Dời marker sang tọa độ khác — soạn bài bấm chọn lại vị trí thì marker nhảy theo ngay. */
        moveMarker(id, x, y, z) {
            const point = toScenePoint(x, y, z);
            return director.moveMarker(id, point.x, point.y, point.z);
        },

        clearMarker() {
            markerFollowMap.clear();
            director.clearMarker();
        },
        focus,

        // ---------- Nhân vật trong cảnh (màn soạn bài dùng để thêm/sửa/xóa ngay trên cảnh) ----------

        /** Có dựng được nhân vật trên loại bối cảnh này không — ảnh/video 360° và ảnh phẳng thì không. */
        canPlaceCharacter: () => !!characterLayer,

        addCharacter: (data) => characterLayer?.add(data),
        updateCharacter: (data) => characterLayer?.update(data),
        removeCharacter: (id) => characterLayer?.remove(id),
        selectCharacter: (id) => characterLayer?.select(id),
        hasCharacterInScene: (id) => !!characterLayer?.hasCharacter(id),
        setCharacterScale: (value) => characterLayer?.setSceneScale(value),
        characterPoseInfo: (id) => characterLayer?.poseInfo(id) ?? null,
        getCharacterHeight: (id) => characterLayer?.getHeight(id) ?? 0,
        resetCamera: director.resetCamera,

        /** Số mesh / tam giác của model — dùng để cảnh báo model quá nặng. Trả 0 với ảnh và video 360°. */
        getStat: () => ({ meshCount, triangleCount: Math.round(triangleCount), assetType }),

        // Tự xoay / độ sáng / chụp khung hình dùng chung với các bối cảnh dựng bằng code — xem js/sim-kit.js.
        // canAutoRotate chỉ bật cho model 3D: xoay một tấm ảnh phẳng là vô nghĩa, còn với ảnh 360° thì camera
        // đứng ở tâm nên "xoay quanh" biến thành cảnh tự quay tít, gây chóng mặt.
        ...createViewerControl({ renderer, controls, scene, camera, canAutoRotate: isModel }),

        /** Ghi đè bản dùng chung: ảnh và video 360° KHÔNG bật nén dải sáng (xem phần Renderer ở trên) nên
         *  toneMappingExposure không có tác dụng gì với chúng — phải chỉnh bằng bộ lọc CSS ngay trên canvas.
         *  Cùng một nút bấm, hai cách thực hiện, để người dùng không phải bận tâm đang xem loại tệp nào. */
        setBrightness(value) {
            const level = Math.max(0.3, Math.min(2.2, Number(value) || 1));
            if (isModel) {
                renderer.toneMappingExposure = level;
                renderer.domElement.style.filter = '';
            } else {
                renderer.domElement.style.filter = level === 1 ? '' : `brightness(${level})`;
            }
            return level;
        },

        setRunning(value) {
            if (value === running) return;
            running = value;
            if (value) { clock.getDelta(); render(); }
            else cancelAnimationFrame(rafId);
        },

        dispose() {
            cancelAnimationFrame(rafId);
            characterLayer?.dispose();
            markerFollowMap.clear();
            resizeObserver.disconnect();
            controls.dispose();
            for (const cleanup of cleanupList) {
                try { cleanup(); } catch (ex) { console.warn('Lỗi khi dọn dẹp cảnh:', ex); }
            }
            renderer.dispose();
            renderer.domElement.remove();
        },
    };
}

// ==================== NẠP TỆP ====================

/** Đuôi tệp model 3D mở được — khớp ExtensionByAssetType["glb"] trong Controllers/SimAssetController.cs. */
export const MODEL_EXTENSION_LIST = ['.glb', '.gltf', '.fbx', '.obj'];

/** Lấy đuôi tệp từ URL, bỏ qua phần ?query — tệp trong wwwroot/uploads có thể kèm tham số chống cache. */
function extensionOf(url) {
    const path = String(url).split('?')[0].split('#')[0];
    const dotIndex = path.lastIndexOf('.');
    return dotIndex < 0 ? '' : path.slice(dotIndex).toLowerCase();
}

/**
 * Nạp model 3D, tự chọn bộ đọc theo đuôi tệp. Luôn trả về cùng một hình dạng { root, animationList }
 * để phần dựng cảnh phía trên không phải biết tệp gốc thuộc định dạng nào.
 *
 * .glb/.gltf là định dạng nên dùng (gọn, mang sẵn vật liệu + hoạt cảnh). .fbx và .obj được hỗ trợ vì đó là
 * thứ bộ phận kỹ thuật thường có sẵn từ phần mềm CAD/3ds Max và không phải ai cũng biết cách chuyển đổi —
 * bắt họ tự chuyển sang .glb trước khi dùng được là rào cản đủ lớn để tính năng không được dùng tới.
 */
async function loadModel(url, renderer, onProgress) {
    const extension = extensionOf(url);
    const report = (event) => {
        if (event && event.total) {
            const percent = Math.round(event.loaded / event.total * 100);
            onProgress(`Đang tải model 3D... ${percent}%`, percent);
        }
    };

    onProgress('Đang tải model 3D...', 0);

    if (extension === '.fbx') {
        const module = await import('three/addons/loaders/FBXLoader.js');
        const group = await new module.FBXLoader().loadAsync(url, report);
        return { root: group, animationList: group.animations || [] };
    }

    if (extension === '.obj') {
        const module = await import('three/addons/loaders/OBJLoader.js');
        const group = await new module.OBJLoader().loadAsync(url, report);
        // .obj để riêng KHÔNG mang theo vật liệu (màu sắc nằm ở tệp .mtl đi kèm, mà hệ thống chỉ nhận một
        // tệp mỗi lần tải lên). OBJLoader gán sẵn vật liệu trắng bóng, đổi sang vật liệu chuẩn để model còn
        // ăn được ánh sáng và env map dựng bên dưới, thay vì hiện ra một khối trắng bẹt không thấy hình khối.
        group.traverse(obj => {
            if (obj.isMesh) obj.material = new THREE.MeshStandardMaterial({ color: 0xb9c2cc, roughness: 0.75, metalness: 0.05 });
        });
        return { root: group, animationList: [] };
    }

    const loader = new GLTFLoader();

    // Model tải từ Sketchfab hay quét bằng điện thoại (Polycam, Scaniverse) hầu hết được nén hình học bằng
    // Draco/Meshopt hoặc nén texture bằng KTX2. Không gắn bộ giải nén thì đúng những tệp phổ biến nhất lại
    // là những tệp báo lỗi. Bọc try/catch vì đây là tải thêm từ CDN: mạng chặn CDN thì chỉ mất khả năng mở
    // tệp NÉN, chứ không được làm hỏng luôn những tệp không nén vốn vẫn mở tốt.
    try {
        const [draco, ktx2, meshopt] = await Promise.all([
            import('three/addons/loaders/DRACOLoader.js'),
            import('three/addons/loaders/KTX2Loader.js'),
            import('three/addons/libs/meshopt_decoder.module.js'),
        ]);
        loader.setDRACOLoader(new draco.DRACOLoader().setDecoderPath(ADDON_CDN + 'draco/gltf/'));
        loader.setKTX2Loader(new ktx2.KTX2Loader().setTranscoderPath(ADDON_CDN + 'basis/').detectSupport(renderer));
        loader.setMeshoptDecoder(meshopt.MeshoptDecoder);
    } catch (ex) {
        console.warn('Không nạp được bộ giải nén model — chỉ mở được model không nén.', ex);
    }

    const gltf = await loader.loadAsync(url, report);
    return { root: gltf.scene, animationList: gltf.animations || [] };
}

function loadImageTexture(url) {
    return new Promise((resolve, reject) => {
        new THREE.TextureLoader().load(url,
            (texture) => {
                texture.colorSpace = THREE.SRGBColorSpace;
                texture.anisotropy = 8;
                resolve({ texture, video: null, cleanup: () => texture.dispose() });
            },
            undefined,
            () => reject(new Error('Không tải được ảnh bối cảnh. Hãy kiểm tra lại tệp đã tải lên.')));
    });
}

function loadVideoTexture(url) {
    return new Promise((resolve, reject) => {
        const video = document.createElement('video');
        video.src = url;
        video.loop = true;
        video.muted = true;          // bắt buộc: trình duyệt chỉ cho tự phát video đã tắt tiếng
        video.playsInline = true;
        video.preload = 'auto';

        video.addEventListener('loadeddata', () => {
            const texture = new THREE.VideoTexture(video);
            texture.colorSpace = THREE.SRGBColorSpace;
            video.play().catch(() => {});
            resolve({
                texture, video,
                cleanup: () => { video.pause(); video.removeAttribute('src'); video.load(); texture.dispose(); },
            });
        }, { once: true });

        video.addEventListener('error', () => reject(new Error('Không tải được video 360°.')), { once: true });
        video.load();
    });
}

/** Nền chuyển sắc dọc dùng làm phông cho model 3D. */
function gradientTexture(topColor, bottomColor) {
    const canvas = document.createElement('canvas');
    canvas.width = 2;
    canvas.height = 256;
    const ctx = canvas.getContext('2d');
    const gradient = ctx.createLinearGradient(0, 0, 0, 256);
    gradient.addColorStop(0, topColor);
    gradient.addColorStop(1, bottomColor);
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, 2, 256);
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    return texture;
}
