// File: Scripts/build-character.mjs
// ============================================================================================
// GỘP NHIỀU TƯ THẾ MIXAMO THÀNH MỘT TỆP NHÂN VẬT DUY NHẤT
//
// Chạy:  node Scripts/build-character.mjs [tên-bộ-nhân-vật]
//        (không truyền gì = "staff", tức wwwroot/models/character/staff.glb)
//
// CẦN GÌ TRƯỚC KHI CHẠY
//   npm i -D fbx2gltf @gltf-transform/core @gltf-transform/functions
//
// LẤY TỆP Ở ĐÂU (mixamo.com, tài khoản Adobe miễn phí, không đòi ghi công khi dùng trong sản phẩm)
//   1. Chọn một nhân vật  ->  Download  ->  Format FBX Binary, Pose "T-pose"
//      Lưu thành:  Scripts/character-source/<bộ>/base.fbx      (đây là NGƯỜI: có da, có xương)
//
//      BỎ QUA ĐƯỢC BƯỚC NÀY nếu bộ đó đã có sẵn tệp wwwroot/models/character/<bộ>.glb: script sẽ lấy
//      chính tệp đó làm người và chỉ gắn thêm tư thế. Đây mới là đường thường dùng — thêm một động tác
//      cho bộ đang chạy thì không việc gì phải tải lại cả nhân vật, và quan trọng hơn là GIỮ NGUYÊN tên
//      mesh trang phục (Ch17_Helmet, Ch17_Vest...) mà hai bộ "văn phòng"/"công trường" đang dựa vào để
//      ẩn/hiện mũ và áo phản quang.
//   2. Với từng tư thế cần dùng, tìm animation tương ứng rồi Download với
//      **Skin = Without Skin** (chỉ lấy chuyển động, không kèm lại thân người — kèm lại thì tệp phình
//      lên gấp mấy lần mà nội dung thừa hoàn toàn).
//      Lưu thành:  Scripts/character-source/<bộ>/<tên-tư-thế>.fbx
//
//      TÊN TỆP CHÍNH LÀ TÊN TƯ THẾ, và phải nằm trong danh sách POSE_LIST của
//      wwwroot/js/sim-characters.js — đặt sai tên thì tệp vẫn gộp được nhưng giao diện soạn bài không
//      chọn tới được đoạn đó. Các tên đang dùng:
//          idle.fbx  sit.fbx  walk.fbx  drink.fbx  eat.fbx  talk.fbx  type.fbx  point.fbx  phone.fbx
//      Gợi ý tên animation trên Mixamo: Idle / Sitting / Walking / Drinking / Eating /
//      Talking / Typing / Pointing / Talking On Phone.
//
// KẾT QUẢ
//   wwwroot/models/character/<bộ>.glb — một tệp, nhiều đoạn animation, mỗi đoạn mang đúng tên tư thế.
//   Không cần sửa một dòng code nào: sim-characters.js dò đoạn theo tên lúc dựng cảnh.
//
// LƯU Ý VỀ CÂN NẶNG: bản Mixamo gốc nhúng texture 4K, một nhân vật có thể tới 50 MB. Người học thường
// dùng mạng công ty và một cảnh có thể có hàng chục người (tuy chỉ tải tệp một lần) — script này thu
// texture về 1024px như đã làm với worker.glb, xem wwwroot/models/README.txt.
// ============================================================================================

import { execFileSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { Document, NodeIO } from '@gltf-transform/core';
import { dedup, mergeDocuments, prune, textureCompress, unpartition } from '@gltf-transform/functions';
import sharp from 'sharp';

const rootDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const characterKey = process.argv[2] || 'staff';
const sourceDir = path.join(rootDir, 'Scripts', 'character-source', characterKey);
const outputPath = path.join(rootDir, 'wwwroot', 'models', 'character', `${characterKey}.glb`);
const workDir = path.join(sourceDir, '.work');

if (!existsSync(sourceDir)) {
    console.error(`Không có thư mục ${sourceDir}.`);
    console.error('Xem phần hướng dẫn ở đầu tệp này để biết tải gì từ mixamo.com và đặt tên ra sao.');
    process.exit(1);
}

// Người để gắn tư thế lên: ưu tiên base.fbx mới tải, không có thì dùng chính tệp .glb đang chạy.
const basePath = path.join(sourceDir, 'base.fbx');
const baseIsFbx = existsSync(basePath);
if (!baseIsFbx && !existsSync(outputPath)) {
    console.error(`Thiếu cả ${basePath} lẫn ${outputPath}.`);
    console.error('Cần một trong hai: tệp NGƯỜI tải ở tab Characters của Mixamo (pose T-pose, đặt tên');
    console.error('base.fbx), hoặc một bộ nhân vật đã dựng sẵn để gắn thêm tư thế vào.');
    process.exit(1);
}

/**
 * Tiền tố tên xương của một tài liệu — "mixamorig:" hay "mixamorig1:" — hoặc null nếu không phải rig Mixamo.
 *
 * Mixamo xuất hai kiểu tên tùy nơi tải: tệp tải ở tab Characters (và mọi thứ dựng lại từ nó) mang
 * "mixamorig1:", còn tệp animation tải ở tab Animations mang "mixamorig:". Đây là cùng một bộ xương, chỉ
 * khác cái tên.
 */
function bonePrefixOf(doc) {
    for (const node of doc.getRoot().listNodes()) {
        const match = /^(mixamorig\d*:)/.exec(node.getName() || '');
        if (match) return match[1];
    }
    return null;
}

/**
 * Đường dẫn công cụ FBX2glTF.
 *
 * Gói npm "fbx2gltf" chỉ kèm sẵn tệp chạy trong node_modules chứ KHÔNG tạo lệnh trong node_modules/.bin —
 * nên "npx FBX2glTF" không gọi tới nó mà bỏ đi hỏi registry, rồi chết vì trên registry không có gói tên
 * viết hoa như vậy. Trỏ thẳng vào tệp chạy, và chỉ khi không thấy mới mượn bản cài toàn máy (nếu có).
 */
function resolveFbxTool() {
    const packaged = path.join(rootDir, 'node_modules', 'fbx2gltf', 'bin', os.type(),
        process.platform === 'win32' ? 'FBX2glTF.exe' : 'FBX2glTF');
    if (existsSync(packaged)) {
        // Bản tải qua npm trên macOS/Linux hay mất cờ chạy.
        if (process.platform !== 'win32') { try { chmodSync(packaged, 0o755); } catch { /* đã đúng quyền */ } }
        return packaged;
    }
    console.warn('  (không thấy fbx2gltf trong node_modules — thử bản cài toàn máy)');
    return 'FBX2glTF';
}

const fbxTool = resolveFbxTool();

/** Chuyển một .fbx sang .glb tạm bằng FBX2glTF. */
function fbxToGlb(inputPath, name) {
    const outPath = path.join(workDir, name);
    console.log(`  FBX -> glTF: ${path.basename(inputPath)}`);
    execFileSync(fbxTool, ['-i', inputPath, '-o', outPath, '-b', '--pbr-metallic-roughness'],
        { stdio: 'inherit' });
    return `${outPath}.glb`;
}

mkdirSync(workDir, { recursive: true });

try {
    const io = new NodeIO();

    console.log(`Dựng bộ nhân vật "${characterKey}"`);
    console.log(baseIsFbx ? '  Người: base.fbx (bản Mixamo mới tải)'
                          : `  Người: ${path.relative(rootDir, outputPath)} (bộ đang chạy, gắn thêm tư thế)`);
    const document = await io.read(baseIsFbx ? fbxToGlb(basePath, 'base') : outputPath);

    // Bản Mixamo qua FBX2glTF luôn cần ba phép sửa vật liệu dưới đây, giống hệt worker.glb — xem
    // wwwroot/models/README.txt để biết vì sao. Bỏ qua thì nhân vật ra tối xỉn và MẤT HẲN phần tóc.
    // Tệp .glb đang chạy thì đã qua bước này rồi, làm lại chỉ thừa.
    if (baseIsFbx) {
        for (const material of document.getRoot().listMaterials()) {
            material.setBaseColorFactor([1, 1, 1, 1]);
            material.setMetallicFactor(0);
            if (material.getAlphaMode() === 'BLEND') material.setAlphaMode('MASK');
        }
    }

    const basePrefix = bonePrefixOf(document);

    // Bảng xương của NGƯỜI, chụp trước khi gộp bất cứ thứ gì — sau khi gộp thì trong tài liệu có nhiều bộ
    // xương trùng tên và không còn phân biệt được đâu là của người.
    const boneByName = new Map(document.getRoot().listNodes().map(n => [n.getName(), n]));

    // Xóa sạch đoạn animation có sẵn: tệp tải ở tab Characters kèm một đoạn T-pose vô dụng, còn tệp .glb
    // đang chạy thì mang các tư thế của lần dựng trước. Bỏ hết đi để THƯ MỤC NGUỒN là nơi duy nhất quyết
    // định bộ này có những tư thế nào — nếu không, mỗi lần chạy lại là một lần chồng thêm đoạn trùng tên.
    for (const animation of document.getRoot().listAnimations()) animation.dispose();

    const poseFileList = readdirSync(sourceDir)
        .filter(f => f.toLowerCase().endsWith('.fbx') && f.toLowerCase() !== 'base.fbx')
        .sort();

    if (poseFileList.length === 0)
        console.warn('  (Chưa có tệp tư thế nào — tệp kết quả sẽ chỉ có người, không có động tác.)');

    for (const fileName of poseFileList) {
        const poseName = path.basename(fileName, path.extname(fileName)).toLowerCase();
        const poseGlb = fbxToGlb(path.join(sourceDir, fileName), poseName);
        const poseDocument = await io.read(poseGlb);

        const animationList = poseDocument.getRoot().listAnimations();
        if (animationList.length === 0) {
            console.warn(`  ! ${fileName} không chứa animation nào — bỏ qua.`);
            continue;
        }

        // GIÓNG LẠI TÊN XƯƠNG trước khi gộp. Đoạn animation trong glTF trỏ vào các NODE của chính tệp
        // tư thế, và lúc dựng cảnh three.js gán đoạn đó vào nhân vật BẰNG TÊN. Tên hai bên lệch nhau
        // ("mixamorig:Hips" so với "mixamorig1:Hips") thì tệp vẫn gộp trót lọt, vẫn thấy đủ tư thế trong
        // danh sách, mà nhân vật đứng chết không nhúc nhích — không một lời báo lỗi nào.
        const posePrefix = bonePrefixOf(poseDocument);
        if (basePrefix && posePrefix && posePrefix !== basePrefix) {
            for (const node of poseDocument.getRoot().listNodes()) {
                const name = node.getName() || '';
                if (name.startsWith(posePrefix)) node.setName(basePrefix + name.slice(posePrefix.length));
            }
            console.log(`    gióng tên xương: ${posePrefix} -> ${basePrefix}`);
        }

        // Chỉ lấy đoạn ĐẦU TIÊN: một tệp tải theo kiểu "Without Skin" luôn chỉ có đúng một đoạn, có thêm
        // đoạn nữa nghĩa là tải nhầm kiểu và các đoạn sau là rác.
        //
        // mergeDocuments trả về bảng tra từ thứ CŨ sang thứ MỚI, nên lấy đúng đoạn vừa gộp qua bảng đó chứ
        // đừng đếm ngược từ cuối danh sách: gộp còn kéo theo cả những thứ khác, đếm nhầm một nhịp là đặt
        // tên tư thế lên nhầm đoạn mà chẳng có gì báo.
        const copyMap = mergeDocuments(document, poseDocument);
        const animation = copyMap.get(animationList[0]);
        animation.setName(poseName);

        // TRỎ LẠI TỪNG KÊNH VỀ ĐÚNG XƯƠNG CỦA NGƯỜI.
        //
        // Gộp xong thì đoạn animation vẫn đang điều khiển bộ xương ĐI KÈM TỆP TƯ THẾ, tức là một bộ xương
        // thứ hai nằm lẫn trong tệp, không dính vào da thịt ai cả. Tệp ghi ra trông vẫn đủ tư thế, mở lên
        // vẫn chọn được, mà người thì đứng chết.
        //
        // Tệ hơn nữa là nó IM LẶNG theo một kiểu rất khó lần: three.js thấy hai node trùng tên nên tự thêm
        // đuôi cho bộ thứ hai ("mixamorig1:Hips" -> "mixamorig1Hips_1"), rồi tìm xương theo cái tên có đuôi
        // đó và không thấy. Lỗi chỉ hiện dưới dạng một xấp cảnh báo PropertyBinding trong Console.
        const missingBoneList = [];
        for (const channel of animation.listChannels()) {
            const boneName = channel.getTargetNode()?.getName();
            const bone = boneName ? boneByName.get(boneName) : null;
            if (bone) channel.setTargetNode(bone);
            else if (boneName) missingBoneList.push(boneName);
        }

        console.log(`  + tư thế "${poseName}" (${animation.listChannels().length} kênh)`);
        if (missingBoneList.length) {
            // Không dừng: thiếu vài xương lẻ (mắt, ngón tay) thì động tác vẫn diễn được gần đủ.
            console.warn(`    ! ${missingBoneList.length} xương không có trong người, bỏ qua: `
                + `${missingBoneList.slice(0, 6).join(', ')}${missingBoneList.length > 6 ? '...' : ''}`);
        }
    }

    // Mỗi tệp tư thế gộp vào còn kéo theo cả một "cảnh" riêng chứa bộ xương thừa của nó. Bỏ hết, chỉ giữ
    // cảnh của người — để lại thì prune() không dám dọn bộ xương thừa (vẫn có cảnh trỏ tới), và tệp phình
    // thêm mấy chục node chẳng ai dùng.
    const mainScene = document.getRoot().getDefaultScene() ?? document.getRoot().listScenes()[0];
    for (const sceneNode of document.getRoot().listScenes()) {
        if (sceneNode !== mainScene) sceneNode.dispose();
    }

    // Không nén lại ảnh của một tệp .glb đã qua bước nén: mỗi lượt WebP -> WebP lại bào mòn thêm một
    // ít chi tiết, mà chạy script vài lần để thêm tư thế là chuyện bình thường.
    await document.transform(
        dedup(),
        prune(),
        // Mỗi tệp tư thế gộp vào mang theo kho dữ liệu nhị phân riêng, mà một tệp .glb chỉ được phép có
        // đúng một kho — không gộp lại thì tới lúc ghi ra là chết với "GLB must have 0–1 buffers".
        unpartition(),
        ...(baseIsFbx ? [textureCompress({ encoder: sharp, targetFormat: 'webp', resize: [1024, 1024] })] : []),
    );

    mkdirSync(path.dirname(outputPath), { recursive: true });
    await io.write(outputPath, document);

    const poseNameList = document.getRoot().listAnimations().map(a => a.getName());
    console.log(`\nXong: ${outputPath}`);
    console.log(`Tư thế trong tệp: ${poseNameList.join(', ') || '(không có)'}`);
    console.log('Mở lại màn soạn mô phỏng là chọn được ngay, không phải sửa code.');
} finally {
    rmSync(workDir, { recursive: true, force: true });
}
