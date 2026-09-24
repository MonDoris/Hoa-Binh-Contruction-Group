// File: wwwroot/js/sim-characters.js
// ============================================================================================
// LỚP NHÂN VẬT CHO CẢNH 3D
//
// Đặt người vào cảnh và cho họ diễn một tư thế (đứng, ngồi, đi, uống, ăn...). Dùng chung cho hai màn hình:
//   * MÀN SOẠN BÀI (pages/course-detail.html) — thêm/sửa/xóa người ngay trên cảnh đang xem.
//   * MÀN HỌC (pages/simulation.html) — dựng lại đúng những người đã lưu, chỉ xem.
//
// VÌ SAO TƯ THẾ LÀ MỘT ĐOẠN ANIMATION, KHÔNG PHẢI MỘT PHÉP XOAY XƯƠNG TÍNH BẰNG CODE
// Xoay hông 90° rồi gập gối 90° thì ra một hình người ngồi — nhưng ngồi kiểu ma-nơ-canh: vai không chùng,
// cột sống không cong, hai tay lơ lửng. Người xem nhận ra ngay là giả. Một đoạn animation quay từ diễn
// viên thật mang theo toàn bộ những chi tiết đó, và đổi tư thế chỉ là đổi một chuỗi ký tự.
//
// TỆP NHÂN VẬT chứa nhiều đoạn animation, mỗi đoạn một tư thế (xem wwwroot/models/README.txt về cách gộp
// nhiều bản tải từ Mixamo vào một tệp). Tư thế nào tệp CHƯA có thì rơi về "đứng yên" — cảnh vẫn chạy,
// người vẫn có mặt, chỉ là chưa diễn đúng động tác. Không bao giờ để nhân vật đứng chết ở tư thế chữ T.
// ============================================================================================

import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { clone as cloneSkeleton } from 'three/addons/utils/SkeletonUtils.js';

/**
 * Các tư thế người soạn bài chọn được. `key` phải khớp CharacterPoseList trong ScenarioController.cs.
 *
 * `clipNameList` là các tên đoạn animation được coi là "chính là tư thế này". Phải dò nhiều tên vì tên đoạn
 * do người dựng tệp đặt: bản tải thẳng từ Mixamo tên là "mixamo.com", người gộp tệp thì đặt "sit" hay
 * "Sitting" tùy tay. Dò theo danh sách này giúp một tệp đặt tên kiểu nào cũng dùng được mà không phải sửa code.
 */
export const POSE_LIST = [
    { key: 'idle',  label: 'Đứng yên',        clipNameList: ['idle', 'standing', 'stand', 'mixamo.com'] },
    { key: 'stand', label: 'Đứng nghe/quan sát', clipNameList: ['stand', 'standing', 'idle'] },
    { key: 'sit',   label: 'Ngồi',            clipNameList: ['sit', 'sitting', 'seated', 'sitidle'] },
    { key: 'walk',  label: 'Đi',              clipNameList: ['walk', 'walking'] },
    { key: 'drink', label: 'Uống nước',       clipNameList: ['drink', 'drinking'] },
    { key: 'eat',   label: 'Ăn',              clipNameList: ['eat', 'eating'] },
    { key: 'talk',  label: 'Nói chuyện',      clipNameList: ['talk', 'talking', 'conversation'] },
    { key: 'type',  label: 'Gõ máy tính',     clipNameList: ['type', 'typing'] },
    { key: 'point', label: 'Chỉ tay',         clipNameList: ['point', 'pointing'] },
    { key: 'phone', label: 'Nghe điện thoại', clipNameList: ['phone', 'talkingonphone', 'callingphone'] },
];

export const POSE_LABEL = Object.fromEntries(POSE_LIST.map(p => [p.key, p.label]));

/** Tư thế "đi" là tư thế DUY NHẤT dùng tới lối đi — giao diện soạn bài dựa vào đây để ẩn/hiện phần vẽ lối. */
export const WALKING_POSE = 'walk';

/**
 * BỘ NHÂN VẬT — `key` phải khớp CharacterSetList trong ScenarioController.cs.
 *
 * Một "bộ" KHÔNG đồng nghĩa với một tệp model riêng. Nhân vật Mixamo để mỗi món trang phục ở một mesh riêng
 * (Ch17_Helmet, Ch17_Vest, Ch17_Shirt, Ch17_Pants...), nên chỉ cần ẩn mũ bảo hộ và áo phản quang đi là cùng
 * một tệp cho ra một nhân viên văn phòng áo sơ mi. Nhờ vậy công ty có ngay hai kiểu người mà không phải đi
 * tìm thêm model nào — và vẫn thêm được bộ hoàn toàn khác về sau bằng cách trỏ `file` sang tệp mới.
 *
 * `hidePartList` / `tintPartList` so khớp theo PHẦN CUỐI tên mesh (sau dấu gạch dưới), không phân biệt hoa
 * thường: tên mesh mang tiền tố riêng của từng model ("Ch17_Vest", "Ch42_Vest") nên so cả tên là đổi model
 * một cái hỏng hết.
 */
export const CHARACTER_SET_LIST = [
    {
        key: 'office',
        label: 'Nhân viên văn phòng',
        file: 'staff',
        hidePartList: ['helmet', 'vest'],
        tintPartList: ['shirt'],
    },
    {
        key: 'worker',
        label: 'Công nhân công trường',
        file: 'staff',
        hidePartList: [],
        // Với công nhân thì áo phản quang mới là món nhìn thấy rõ nhất; nhuộm áo sơ mi bên trong gần như
        // không ai thấy vì đã bị áo khoác ngoài che kín.
        tintPartList: ['vest'],
    },
];

/** Màu trang phục để người soạn bài chọn, và cũng là bộ màu hệ thống tự rải khi không ai chọn.
 *
 *  Đều là màu tầm trung: màu áo được NHÂN với ảnh vân vải của model chứ không thay thế nó (giữ được nếp gấp
 *  và bóng đổ), mà phép nhân thì màu càng tối càng ăn mòn hết chi tiết thành một mảng đen. */
export const OUTFIT_COLOR_LIST = [
    '#3b82f6', '#0ea5e9', '#14b8a6', '#22c55e', '#eab308',
    '#f97316', '#ef4444', '#ec4899', '#8b5cf6', '#64748b',
];

/** Tiền tố của bộ nhân vật tải lên Thư viện 3D — khớp LibraryCharacterPrefix trong ScenarioController.cs. */
const LIBRARY_PREFIX = 'asset:';

/**
 * Bộ nhân vật dùng cho một bản ghi đã đặt trong cảnh.
 *
 * Nhân vật TẢI LÊN từ Thư viện 3D không có mục nào trong CHARACTER_SET_LIST — server gửi kèm đường dẫn tệp
 * ở `modelUrl`, còn phần trang phục thì để trống: mỗi model tải về đặt tên mesh một kiểu, không đoán được
 * đâu là áo để nhuộm hay mũ để ẩn. Người vẫn hiện đủ và diễn đủ tư thế, chỉ là không đổi màu áo được.
 */
function resolveSetFor(data) {
    if (String(data.characterKey || '').startsWith(LIBRARY_PREFIX)) {
        return {
            key: data.characterKey,
            label: data.label || 'Nhân vật thư viện',
            url: data.modelUrl || null,
            hidePartList: [],
            tintPartList: [],
        };
    }
    const set = resolveSet(data.characterKey);
    return { ...set, url: characterUrl(set.file) };
}

/** Bộ nhân vật CÀI SẴN theo key, có xử lý key cũ. */
function resolveSet(characterKey) {
    // "staff" là key của dữ liệu đặt trước khi có khái niệm bộ — hiểu là bộ văn phòng, đúng với thứ những
    // nhân vật đó đang hiện ra (tệp staff.glb vốn có sẵn mũ và áo phản quang, nhưng đặt trong văn phòng).
    const key = characterKey === 'staff' || !characterKey ? 'office' : characterKey;
    return CHARACTER_SET_LIST.find(s => s.key === key) ?? CHARACTER_SET_LIST[0];
}

/** Tên bộ phận của một mesh: phần sau dấu gạch dưới cuối cùng, viết thường. "Ch17_Vest" -> "vest". */
function partNameOf(nodeName) {
    return String(nodeName || '').split('_').pop().toLowerCase();
}

/** Màu trang phục của một nhân vật: màu người soạn bài chọn, hoặc rải tự động theo Id.
 *
 *  Rải theo Id chứ không lấy ngẫu nhiên: dựng lại cảnh hai lần phải ra hai cảnh giống hệt nhau, chứ không
 *  phải mỗi lần mở bài học lại thấy cả phòng đổi màu áo. */
function outfitColorOf(data) {
    if (data.outfitColor) return data.outfitColor;
    return OUTFIT_COLOR_LIST[Math.abs(data.id || 0) % OUTFIT_COLOR_LIST.length];
}

/** Chiều cao quy ước của nhân vật sau khi chuẩn hóa, tính bằng đơn vị cảnh.
 *
 *  Mọi tệp nhân vật đều được co về đúng chiều cao này trước khi đặt vào cảnh, nên hệ số tỉ lệ mà người soạn
 *  bài kéo trên thanh trượt luôn có nghĩa như nhau bất kể tệp gốc dựng theo mét hay centimet. Lấy 1 vì cảnh
 *  đã được chuẩn hóa về hộp bao 10 đơn vị (xem MODEL_SIZE trong sim-asset-scene.js): một văn phòng cỡ 15 m
 *  ứng với người cao khoảng 1,1 đơn vị, nên để mặc định 1 là đã gần đúng ngay từ lần đặt đầu tiên. */
const BASE_HEIGHT = 1;

/** Tốc độ đi mặc định (đơn vị cảnh / giây) khi người soạn bài không tự đặt. */
const DEFAULT_WALK_SPEED = 0.6;

/** Làm tròn tọa độ về 3 chữ số thập phân: đủ mịn cho một cảnh rộng 10 đơn vị, mà không sinh ra chuỗi
 *  JSON lối đi dài lê thê chỉ vì sai số dấu phẩy động của phép cộng lúc kéo. */
const round3 = (v) => Math.round(v * 1000) / 1000;

/** Đường dẫn tệp của một bộ nhân vật CÀI SẴN. */
function characterUrl(fileName) {
    return `/models/character/${fileName || 'staff'}.glb`;
}

// Tệp nhân vật tải MỘT lần rồi nhân bản cho mọi người trong cảnh — mỗi cảnh có thể có hàng chục người, tải
// lại từng người là hàng chục lần cùng một tệp vài megabyte.
const sourceCache = new Map();

async function loadCharacterSource(url) {
    if (sourceCache.has(url)) return sourceCache.get(url);

    const promise = (async () => {
        const loader = new GLTFLoader();
        let gltf = null;
        try {
            gltf = await loader.loadAsync(url);
        } catch {
            // Chưa có bộ nhân vật riêng thì mượn người của cảnh công trường — tệp đó chắc chắn có mặt.
            // Người vẫn hiện ra, chỉ là mới có tư thế đứng; đây là trạng thái bình thường trước khi công ty
            // bổ sung các đoạn animation còn lại, không phải lỗi.
            console.warn(`[Nhân vật 3D] Không tải được ${url} — tạm dùng /models/worker.glb.`);
            gltf = await loader.loadAsync('/models/worker.glb');
        }

        const model = gltf.scene;
        model.traverse(node => {
            if (!node.isMesh && !node.isSkinnedMesh) return;
            node.castShadow = true;
            node.receiveShadow = true;
            // Skinned mesh hay bị loại nhầm khỏi khung hình vì hộp bao được tính theo tư thế gốc, không
            // theo tư thế đang diễn — nhân vật ngồi hoặc cúi sẽ chớp tắt khi xoay camera.
            node.frustumCulled = false;
        });

        // Chuẩn hóa chiều cao: đo hộp bao rồi quy về BASE_HEIGHT. Phải đo ở tư thế gốc (chưa gắn animation)
        // vì cùng một người, tư thế ngồi cho hộp bao thấp hơn hẳn tư thế đứng — chuẩn hóa theo tư thế đang
        // diễn sẽ làm người ngồi bỗng to lên cho "đủ chiều cao".
        const box = new THREE.Box3().setFromObject(model);
        const size = box.getSize(new THREE.Vector3());
        const center = box.getCenter(new THREE.Vector3());
        const height = Math.max(size.y, 1e-4);

        // GIÓNG GỐC TỌA ĐỘ VỀ GIỮA HAI BÀN CHÂN, rồi bọc model trong một nhóm ngoài.
        //
        // Cả hệ thống hiểu tọa độ của một nhân vật là CHỖ NGƯỜI ĐÓ ĐỨNG: vòng tròn chọn vẽ ở đó, lối đi đi
        // qua đó, và thao tác kéo thả người xuống đúng điểm chạm sàn. Nhưng gốc tọa độ trong tệp thì mỗi
        // người dựng đặt một kiểu — nhân vật Mixamo đặt ngay dưới chân, còn bản tải trên Sketchfab về hay
        // đặt ngang bụng (hộp bao chạy từ y = -0,95 tới +0,95). Đặt thẳng loại sau xuống sàn là nửa dưới
        // chìm nghỉm dưới đất, chỉ còn cái đầu nhô lên.
        //
        // Phải bọc thêm một nhóm ngoài chứ không dịch thẳng model: nhóm ngoài mới là thứ applyTransform()
        // gán tọa độ vào, mà gán vào chính model thì xóa mất phép dịch này ngay lần chỉnh đầu tiên.
        model.position.add(new THREE.Vector3(-center.x, -box.min.y, -center.z));
        const root = new THREE.Group();
        root.add(model);

        return { root, animationList: gltf.animations || [], normalizeScale: BASE_HEIGHT / height };
    })();

    sourceCache.set(url, promise);
    return promise;
}

/** Chuẩn hóa tên đoạn animation để so khớp: bỏ khoảng trắng, gạch, và phần tiền tố kiểu "Armature|". */
function normalizeClipName(name) {
    return String(name || '').split('|').pop().toLowerCase().replace(/[^a-z0-9]/g, '');
}

/**
 * Tìm đoạn animation ứng với một tư thế.
 * Trả về null khi tệp không có đoạn nào hợp — nơi gọi tự lo phương án dự phòng.
 */
function findClip(animationList, poseKey) {
    if (!animationList.length) return null;

    const pose = POSE_LIST.find(p => p.key === poseKey);
    const wantedList = (pose?.clipNameList ?? [poseKey]).map(normalizeClipName);

    // Khớp đúng tên trước, rồi mới tới khớp một phần: tệp có cả "walk" lẫn "walkbackward" thì "walk" phải
    // thắng, chứ không phải đoạn nào dò thấy trước cũng được.
    for (const wanted of wantedList) {
        const exact = animationList.find(c => normalizeClipName(c.name) === wanted);
        if (exact) return exact;
    }
    for (const wanted of wantedList) {
        const partial = animationList.find(c => normalizeClipName(c.name).includes(wanted));
        if (partial) return partial;
    }
    return null;
}

/** Đọc lối đi đã lưu. JSON hỏng thì coi như không có lối — cảnh vẫn dựng, người đứng tại chỗ. */
function parsePath(pathJson) {
    if (!pathJson) return [];
    try {
        const raw = JSON.parse(pathJson);
        if (!Array.isArray(raw)) return [];
        return raw
            .filter(p => Array.isArray(p) && p.length === 3 && p.every(n => Number.isFinite(n)))
            .map(p => new THREE.Vector3(p[0], p[1], p[2]));
    } catch {
        console.warn('[Nhân vật 3D] Lối đi lưu sai định dạng, bỏ qua:', pathJson);
        return [];
    }
}

/**
 * Tạo lớp nhân vật gắn vào một cảnh Three.js.
 *
 * @param {THREE.Scene} scene cảnh để thêm nhân vật vào
 * @param {object} [option]
 * @param {number} [option.sceneScale] hệ số cỡ người của cả cảnh (ScenarioScene.CharacterScale)
 * @returns bộ điều khiển lớp nhân vật
 */
export function createCharacterLayer(scene, option = {}) {
    let sceneScale = Number(option.sceneScale) || 1;

    /** Mọi nhân vật đang có trong cảnh, tra theo Id bản ghi. */
    const entryMap = new Map();

    /** Tra ngược từ nhóm 3D về Id. Tia bấm chuột chỉ trúng được một mesh con ("Ch17_Vest") nên phải lần
     *  ngược lên cây mới biết mesh đó thuộc về người nào. */
    const idByGroup = new Map();

    /** Vòng tròn sáng dưới chân người đang được chọn ở màn soạn bài. */
    let selectionRing = null;
    let selectedId = null;

    function applyTransform(entry) {
        const data = entry.data;
        const scale = entry.normalizeScale * sceneScale * (Number(data.scale) || 1);
        entry.group.scale.setScalar(scale);

        // Nhân vật đang đi thì vị trí do lối đi quyết định — ghi đè ở đây sẽ giật người về điểm gốc mỗi
        // lần người soạn bài chỉnh một ô bất kỳ.
        if (!entry.pathList.length) {
            entry.group.position.set(data.positionX || 0, data.positionY || 0, data.positionZ || 0);
            entry.group.rotation.y = THREE.MathUtils.degToRad(data.rotationY || 0);
        }
    }

    /**
     * Ẩn/hiện món trang phục theo bộ, và nhuộm màu áo riêng cho từng người.
     *
     * PHẢI nhân bản vật liệu trước khi nhuộm: cloneSkeleton chỉ nhân bản node và xương, còn vật liệu thì DÙNG
     * CHUNG với bản gốc — nhuộm thẳng vào đó là cả phòng đổi màu theo người vừa chỉnh.
     *
     * Vật liệu nhân bản được ghi lại trên chính entry để lần đổi màu sau dùng lại, thay vì mỗi lần kéo thanh
     * trượt lại sinh thêm một vật liệu mới mà không ai giải phóng.
     */
    function applyOutfit(entry) {
        const set = resolveSetFor(entry.data);
        const color = outfitColorOf(entry.data);

        entry.group.traverse(node => {
            if (!node.isMesh && !node.isSkinnedMesh) return;
            const part = partNameOf(node.name);

            node.visible = !set.hidePartList.includes(part);
            if (!set.tintPartList.includes(part)) return;

            if (!entry.tintedMaterialMap.has(node.uuid)) {
                const cloned = Array.isArray(node.material)
                    ? node.material.map(m => m.clone())
                    : node.material.clone();
                node.material = cloned;
                entry.tintedMaterialMap.set(node.uuid, cloned);
            }

            const materialList = entry.tintedMaterialMap.get(node.uuid);
            // Màu này NHÂN với ảnh vân vải chứ không thay thế nó — giữ được nếp gấp và bóng đổ của áo.
            for (const m of (Array.isArray(materialList) ? materialList : [materialList])) m.color.set(color);
        });
    }

    function applyPose(entry) {
        const clip = findClip(entry.animationList, entry.data.poseKey)
            // Không có đoạn đúng tư thế thì lấy đoạn đứng yên; không có nốt thì lấy đoạn đầu tiên của tệp.
            ?? findClip(entry.animationList, 'idle')
            ?? entry.animationList[0]
            ?? null;

        entry.action?.stop();
        entry.action = null;
        if (!clip) return;

        entry.action = entry.mixer.clipAction(clip);
        // Lệch pha mỗi người một chút: cả phòng cùng thở và cùng nhấc tay đúng một nhịp trông như rô-bốt.
        entry.action.time = (entry.phase * clip.duration) % clip.duration;
        entry.action.timeScale = 0.9 + entry.phase * 0.2;
        entry.action.play();
    }

    function syncPath(entry) {
        entry.pathList = entry.data.poseKey === WALKING_POSE ? parsePath(entry.data.pathJson) : [];
        entry.pathIndex = 0;
        entry.pathForward = true;
        if (entry.pathList.length >= 2) entry.group.position.copy(entry.pathList[0]);
    }

    /** Đưa nhân vật đi tiếp một khoảng thời gian dọc lối đi, tự xoay mặt theo hướng đi. */
    function advanceWalk(entry, delta) {
        const pathList = entry.pathList;
        if (pathList.length < 2) return;

        const nextIndex = entry.pathForward ? entry.pathIndex + 1 : entry.pathIndex - 1;
        const target = pathList[nextIndex];
        if (!target) {
            // Hết lối thì quay đầu đi ngược lại thay vì nhảy phắt về điểm đầu — nhảy về chỗ cũ giữa phòng
            // là thứ đập vào mắt ngay lần đầu người học nhìn thấy.
            entry.pathForward = !entry.pathForward;
            return;
        }

        const speed = Number(entry.data.walkSpeed) > 0 ? Number(entry.data.walkSpeed) : DEFAULT_WALK_SPEED;
        const toTarget = target.clone().sub(entry.group.position);
        const distance = toTarget.length();

        if (distance <= speed * delta) {
            entry.group.position.copy(target);
            entry.pathIndex = nextIndex;
            return;
        }

        toTarget.normalize();
        entry.group.position.addScaledVector(toTarget, speed * delta);
        // atan2(x, z) chứ không phải (z, x): trong Three.js nhân vật nhìn theo trục +Z cục bộ của chính nó.
        entry.group.rotation.y = Math.atan2(toTarget.x, toTarget.z);
    }

    // Nhân vật đang trong lúc chờ tải tệp. Cần thiết vì màn soạn bài gọi add() liên tục theo từng nấc kéo
    // thanh trượt: không chặn thì mỗi nấc kéo sinh thêm một bản sao và cảnh đầy người chồng lên nhau.
    const pendingSet = new Set();
    const pendingDataMap = new Map();

    const layer = {
        /** Đặt một nhân vật vào cảnh. `data` là một ScenarioCharacterDto.
         *
         *  Gọi lại với một Id đã có = cập nhật chứ không nhân bản, kể cả khi lần gọi trước còn đang tải tệp. */
        async add(data) {
            if (entryMap.has(data.id)) { layer.update(data); return entryMap.get(data.id); }
            if (pendingSet.has(data.id)) { pendingDataMap.set(data.id, data); return null; }
            pendingSet.add(data.id);

            let source;
            try {
                const set = resolveSetFor(data);
                // Nhân vật thư viện mà thiếu đường dẫn tệp thì rơi về bộ cài sẵn — người vẫn hiện ra nhưng là
                // MỘT NGƯỜI KHÁC HẲN người vừa chọn. Phải kêu lên, vì nhìn vào cảnh không tài nào đoán được.
                if (!set.url) {
                    console.warn('[Nhân vật 3D] Bộ nhân vật', data.characterKey,
                        'không có modelUrl — tạm dùng bộ cài sẵn. Nơi gọi phải gửi kèm modelUrl.');
                }
                source = await loadCharacterSource(set.url || characterUrl('staff'));
            } finally {
                pendingSet.delete(data.id);
            }

            const group = cloneSkeleton(source.root);
            const entry = {
                data: { ...data },
                group,
                mixer: new THREE.AnimationMixer(group),
                animationList: source.animationList,
                normalizeScale: source.normalizeScale,
                action: null,
                pathList: [],
                pathIndex: 0,
                pathForward: true,
                tintedMaterialMap: new Map(),
                // Đang bị cầm kéo. Người đang kéo thì lối đi phải nhường quyền điều khiển vị trí, nếu không
                // mỗi khung hình nó lại lôi người về lối cũ và người dính chặt lấy con trỏ.
                dragging: false,
                // Cố định theo Id chứ không lấy ngẫu nhiên: dựng lại cảnh hai lần phải ra hai cảnh giống hệt
                // nhau, nếu không người soạn bài chỉnh xong bấm xem lại sẽ thấy mọi người lệch nhịp khác đi.
                phase: ((data.id || 0) * 0.37) % 1,
            };

            scene.add(group);
            entryMap.set(data.id, entry);
            idByGroup.set(group, data.id);
            syncPath(entry);
            applyTransform(entry);
            applyOutfit(entry);
            applyPose(entry);

            // Trong lúc chờ tải tệp, người soạn bài có thể đã kéo/xoay/đổi tư thế thêm mấy lần — áp bản mới
            // nhất, nếu không nhân vật hiện ra ở đúng trạng thái của vài giây trước.
            if (pendingDataMap.has(data.id)) {
                layer.update(pendingDataMap.get(data.id));
                pendingDataMap.delete(data.id);
            }
            return entry;
        },

        /** Cập nhật một nhân vật đã có (đổi tư thế, dời chỗ, xoay, đổi cỡ, sửa lối đi). */
        update(data) {
            const entry = entryMap.get(data.id);
            if (!entry) return;

            const poseChanged = entry.data.poseKey !== data.poseKey;
            const pathChanged = entry.data.pathJson !== data.pathJson;
            const outfitChanged = entry.data.characterKey !== data.characterKey
                                || entry.data.outfitColor !== data.outfitColor;
            const oldKey = entry.data.characterKey;
            const oldUrl = resolveSetFor(entry.data).url;
            entry.data = { ...data };

            // Đổi sang một bộ dùng TỆP MODEL khác thì không vá được trên bản đang có — phải dựng lại người.
            // Hai bộ thư viện khác nhau cũng phải dựng lại kể cả khi đường dẫn tra ra giống nhau (cùng thiếu
            // modelUrl chẳng hạn) — nếu chỉ so đường dẫn thì đổi ô chọn xong cảnh vẫn y nguyên người cũ.
            const isLibrary = (key) => String(key || '').startsWith(LIBRARY_PREFIX);
            if (oldUrl !== resolveSetFor(data).url
                || ((isLibrary(oldKey) || isLibrary(data.characterKey)) && oldKey !== data.characterKey)) {
                layer.remove(data.id);
                layer.add(data).catch(ex => console.warn('[Nhân vật 3D] Không đổi được bộ nhân vật', ex));
                return;
            }

            if (poseChanged || pathChanged) syncPath(entry);
            applyTransform(entry);
            if (outfitChanged) applyOutfit(entry);
            if (poseChanged) applyPose(entry);
        },

        remove(id) {
            // Bỏ luôn khỏi hàng chờ: người vừa bị xóa mà tệp còn đang tải thì lát nữa vẫn hiện ra.
            pendingSet.delete(id);
            pendingDataMap.delete(id);

            const entry = entryMap.get(id);
            if (!entry) return;
            entry.action?.stop();
            entry.mixer.uncacheRoot(entry.group);
            scene.remove(entry.group);
            idByGroup.delete(entry.group);
            disposeTintedMaterial(entry);
            entryMap.delete(id);
            if (selectedId === id) layer.select(null);
        },

        /** Đổi cỡ người cho CẢ cảnh — người soạn bài kéo thanh trượt, mọi người phải đổi theo ngay lập tức. */
        setSceneScale(value) {
            sceneScale = Number(value) || 1;
            entryMap.forEach(applyTransform);
        },

        /** Làm nổi nhân vật đang chọn bằng một vòng tròn dưới chân (chỉ dùng ở màn soạn bài). */
        select(id) {
            selectedId = id;
            const entry = id == null ? null : entryMap.get(id);

            if (!entry) {
                if (selectionRing) selectionRing.visible = false;
                return;
            }

            if (!selectionRing) {
                selectionRing = new THREE.Mesh(
                    new THREE.RingGeometry(0.28, 0.36, 32).rotateX(-Math.PI / 2),
                    new THREE.MeshBasicMaterial({ color: 0x38bdf8, transparent: true, opacity: 0.9,
                        depthTest: false, side: THREE.DoubleSide }));
                // Vẽ sau cùng và bỏ kiểm tra chiều sâu: vòng tròn nằm sát sàn nên rất dễ bị chính mặt sàn
                // che mất, mà đây lại là thứ duy nhất cho biết đang chỉnh người nào.
                selectionRing.renderOrder = 999;
                scene.add(selectionRing);
            }
            selectionRing.visible = true;
        },

        /** Chiều cao thực tế của một nhân vật trong cảnh (đơn vị cảnh).
         *
         *  Không đo lại hộp bao: hộp bao đổi theo tư thế đang diễn, người ngồi sẽ cho ra chiều cao thấp hơn
         *  và dấu tình huống treo trên đầu họ sẽ nhấp nhô theo từng nhịp animation. */
        getHeight(id) {
            const entry = entryMap.get(id);
            if (!entry) return 0;
            return BASE_HEIGHT * sceneScale * (Number(entry.data.scale) || 1);
        },

        /** Vị trí NGANG ĐẦU của một nhân vật — chỗ treo dấu tình huống gắn vào người đó.
         *
         *  Treo ngang đầu chứ không phải dưới chân: dấu đặt sát sàn thì chìm vào mặt sàn và bị bàn ghế che,
         *  đúng những chỗ đông đồ đạc nhất lại là chỗ hay có người nhất. Trả về null khi không còn nhân vật
         *  đó — nơi gọi phải rơi về tọa độ cố định đã lưu chứ đừng bỏ luôn tình huống. */
        getWorldPosition(id, target = new THREE.Vector3()) {
            const entry = entryMap.get(id);
            if (!entry) return null;

            entry.group.getWorldPosition(target);
            target.y += BASE_HEIGHT * sceneScale * (Number(entry.data.scale) || 1) * 1.05;
            return target;
        },

        hasCharacter: (id) => entryMap.has(id),

        /**
         * Nhân vật đang nằm dưới tia bấm chuột, hoặc null.
         *
         * Chỉ tính món trang phục đang HIỆN: tia của Three.js không quan tâm tới `visible`, nên bộ văn phòng
         * tuy đã ẩn mũ bảo hộ nhưng tia vẫn xuyên trúng nó — không lọc thì bấm vào khoảng không phía trên
         * đầu người cũng cầm được người lên.
         */
        pickCharacter(raycaster) {
            const groupList = [];
            entryMap.forEach(entry => groupList.push(entry.group));
            if (!groupList.length) return null;

            for (const hit of raycaster.intersectObjects(groupList, true)) {
                if (!hit.object.visible) continue;
                for (let node = hit.object; node; node = node.parent) {
                    if (idByGroup.has(node)) return idByGroup.get(node);
                }
            }
            return null;
        },

        /**
         * Tệp của nhân vật này diễn được những tư thế nào.
         *
         * Tư thế là một ĐOẠN ANIMATION có sẵn trong tệp, không phải phép tính trên xương (xem đầu tệp này),
         * nên tệp không có đoạn nào thì bấm tư thế gì người cũng đứng bất động — mà nhìn vào cảnh thì không
         * tài nào phân biệt được "tệp thiếu hoạt cảnh" với "chức năng hỏng". Màn soạn bài gọi hàm này để nói
         * thẳng ra điều đó.
         *
         * @returns {{clipCount:number, poseKeyList:string[]}|null} poseKeyList là các tư thế trong POSE_LIST
         *          thật sự dò ra đoạn animation; null nếu không còn nhân vật đó trong cảnh.
         */
        poseInfo(id) {
            const entry = entryMap.get(id);
            if (!entry) return null;
            return {
                clipCount: entry.animationList.length,
                poseKeyList: POSE_LIST.filter(p => findClip(entry.animationList, p.key)).map(p => p.key),
            };
        },

        /** Bật/tắt trạng thái đang bị cầm kéo. */
        setDragging(id, on) {
            const entry = entryMap.get(id);
            if (entry) entry.dragging = !!on;
        },

        /** Chỗ ĐANG ĐỨNG của một nhân vật (dưới chân, tọa độ cảnh), hoặc null nếu không còn người đó.
         *
         *  Khác getWorldPosition() ở chỗ không cộng thêm chiều cao: bên kia trả về điểm ngang đầu để treo
         *  dấu tình huống, còn thao tác kéo thì cần đúng điểm chạm đất. */
        getPosition(id, target = new THREE.Vector3()) {
            const entry = entryMap.get(id);
            return entry ? target.copy(entry.group.position) : null;
        },

        /**
         * Dời một nhân vật tới một điểm trong cảnh — thao tác KÉO RÊ ở màn soạn bài.
         *
         * Người có LỐI ĐI thì kéo cả lối đi theo chứ không chỉ điểm đứng: kéo mà bỏ lối lại chỗ cũ thì vừa
         * buông tay người đã chạy ngược về lối cũ, nhìn y như thao tác kéo không ăn.
         *
         * Cộng theo ĐỘ LỆCH chứ không gán thẳng tọa độ mới: người đang đi thì vị trí thấy trên màn hình là
         * một điểm nào đó giữa lối đi, không phải điểm đứng đã lưu — gán thẳng là điểm đứng nhảy đi một
         * quãng đúng bằng phần người đó đã đi được.
         *
         * @returns trạng thái mới để nơi gọi ghi vào biểu mẫu rồi lưu lên server, hoặc null nếu không còn
         *          nhân vật đó.
         */
        dragTo(id, x, y, z) {
            const entry = entryMap.get(id);
            if (!entry) return null;

            const deltaX = x - entry.group.position.x;
            const deltaY = y - entry.group.position.y;
            const deltaZ = z - entry.group.position.z;
            entry.group.position.set(x, y, z);

            entry.data.positionX = round3((entry.data.positionX || 0) + deltaX);
            entry.data.positionY = round3((entry.data.positionY || 0) + deltaY);
            entry.data.positionZ = round3((entry.data.positionZ || 0) + deltaZ);

            for (const point of entry.pathList)
                point.set(round3(point.x + deltaX), round3(point.y + deltaY), round3(point.z + deltaZ));
            if (entry.pathList.length >= 2)
                entry.data.pathJson = JSON.stringify(entry.pathList.map(p => [p.x, p.y, p.z]));

            return {
                positionX: entry.data.positionX,
                positionY: entry.data.positionY,
                positionZ: entry.data.positionZ,
                pathJson: entry.data.pathJson || null,
            };
        },

        /** Gọi mỗi khung hình. `delta` tính bằng giây. */
        tick(delta) {
            entryMap.forEach(entry => {
                entry.mixer.update(delta);
                if (entry.pathList.length >= 2 && !entry.dragging) advanceWalk(entry, delta);
            });

            if (selectionRing?.visible && selectedId != null) {
                const entry = entryMap.get(selectedId);
                if (entry) {
                    selectionRing.position.copy(entry.group.position);
                    selectionRing.position.y += 0.01;
                    selectionRing.scale.setScalar(Math.max(sceneScale * (Number(entry.data.scale) || 1), 0.2));
                }
            }
        },

        dispose() {
            pendingSet.clear();
            pendingDataMap.clear();
            entryMap.forEach(entry => {
                entry.action?.stop();
                entry.mixer.uncacheRoot(entry.group);
                scene.remove(entry.group);
                disposeTintedMaterial(entry);
            });
            entryMap.clear();
            idByGroup.clear();
            if (selectionRing) {
                scene.remove(selectionRing);
                selectionRing.geometry.dispose();
                selectionRing.material.dispose();
                selectionRing = null;
            }
        },
    };

    /** Chỉ giải phóng vật liệu do chính lớp này nhân bản ra. Vật liệu gốc thuộc về tệp model đang nằm trong
     *  bộ nhớ đệm và còn được mọi nhân vật khác dùng chung — giải phóng nhầm là những người còn lại mất
     *  vân vải, hiện ra trắng bệch. */
    function disposeTintedMaterial(entry) {
        entry.tintedMaterialMap?.forEach(material => {
            for (const m of (Array.isArray(material) ? material : [material])) m.dispose();
        });
        entry.tintedMaterialMap?.clear();
    }

    return layer;
}
