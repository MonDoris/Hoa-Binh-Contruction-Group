// File: wwwroot/js/sim-registry.js
// ============================================================================================
// DANH MỤC BỐI CẢNH 3D
//
// Mỗi ScenarioScene trong CSDL có một cột SCENE_KEY trỏ tới một mục ở đây. Trang pages/simulation.html
// chỉ chứa nghiệp vụ bài học (gọi API, chấm điểm, bảng xử lý tình huống) và hoàn toàn không biết cảnh
// nào đang được dựng — nó tra key trong bảng này rồi gọi hàm dựng tương ứng.
//
// KHÔNG PHẢI LÚC NÀO CŨNG CẦN THÊM BỐI CẢNH MỚI Ở ĐÂY: nếu chỉ cần một không gian lấy từ ảnh/model có
// sẵn thì nghiệp vụ tự chọn bối cảnh "custom" trên màn soạn bài rồi tải tệp lên — xem mục 'custom' và
// js/sim-asset-scene.js. Chỉ dựng cảnh bằng code (các bước dưới đây) khi cần một không gian có hoạt
// cảnh và tương tác riêng mà một tệp tĩnh không diễn tả được.
//
// THÊM MỘT BỐI CẢNH DỰNG BẰNG CODE cần đúng 4 việc:
//   1. Viết js/sim-<tên>.js xuất một hàm dựng cảnh nhận (container, onProgress) và trả về đối tượng
//      { addMarker, focus, resetCamera, setRunning, dispose } — dùng js/sim-kit.js cho phần dùng chung.
//   2. Thêm một mục vào SIM_SCENE_MAP dưới đây.
//   3. Thêm key đó vào mảng SceneKeyList trong Controllers/ScenarioController.cs (server kiểm tính hợp lệ)
//      và vào SCENARIO_SCENE_LIST trong pages/course-detail.html (mốc tọa độ dựng sẵn của cảnh).
//   4. Thêm key đó vào SeedBuiltinSimSceneAsync trong Data/DbSeeder.cs — đây là bản ghi đưa bối cảnh vào
//      THƯ VIỆN 3D (pages/sim-library.html). Thiếu bước này thì cảnh vẫn chạy được nhưng không ai xem trước
//      hay đổi tên nó được, và nó vắng mặt trong kho bối cảnh dùng chung của công ty.
//
// PHÂN VAI GIỮA FILE NÀY VÀ THƯ VIỆN 3D: mã nguồn quyết định bối cảnh nào DỰNG ĐƯỢC, còn bản ghi trong thư
// viện (bảng SimBuiltinScene) quyết định nó ĐƯỢC GỌI LÀ GÌ và mô tả ra sao — tên trong thư viện sửa được
// ngay trên giao diện, nên chuỗi `label` dưới đây chỉ còn là phương án dự phòng khi chưa có bản ghi.
//
// Hàm dựng được nạp bằng import() động: người học vào kịch bản nhân sự thì không phải tải mã của cảnh
// công trường và ngược lại. Trang Thư viện 3D dùng lại đúng các hàm này để xem trước, nên cảnh trong thư
// viện và cảnh lúc học luôn là MỘT.
// ============================================================================================

export const SIM_SCENE_MAP = {
    'construction': {
        label: 'Công trường xây dựng',
        // Thuật ngữ hiển thị trên giao diện — kịch bản công trường nói "điểm nguy cơ", kịch bản quy trình
        // nói "bước", nên phần chữ phải đổi theo bối cảnh chứ không cứng trong trang.
        pointNoun: 'điểm nguy cơ',
        loadingText: 'Đang khởi tạo cảnh 3D công trường...',
        hintHtml: 'Kéo để xoay · lăn chuột để phóng to · bấm dấu <b>!</b> để xử lý điểm nguy cơ',
        // Tab tư liệu tham khảo (tùy chọn): một model bên thứ ba nhúng qua iframe Sketchfab.
        reference: {
            tabLabel: 'Mô hình công trình',
            tabIcon: 'ph-buildings',
            modelId: '640916ab6ceb4589805ca4df24372cad',
            title: 'Under Construction Building',
            author: 'nuralam018',
        },
        async load() {
            const module = await import('/js/sim-scene.js');
            return module.createSimWorld;
        },
    },

    'hr-office': {
        label: 'Văn phòng phòng Nhân sự',
        pointNoun: 'bước',
        loadingText: 'Đang khởi tạo văn phòng phòng Nhân sự...',
        hintHtml: 'Kéo để xoay · lăn chuột để phóng to · bấm dấu <b>!</b> để xử lý bước tiếp theo',
        reference: null,
        async load() {
            const module = await import('/js/sim-hr-scene.js');
            return module.createHrWorld;
        },
    },

    // Bối cảnh do chính nghiệp vụ tải tệp lên (model .glb, ảnh/video 360°, ảnh thường). KHÔNG cần viết
    // thêm file dựng cảnh nào cho mỗi kịch bản mới: sim-asset-scene.js dựng cảnh thẳng từ tệp đã tải lên,
    // còn vị trí từng tình huống thì người soạn bài bấm chuột chọn ngay trên cảnh đó.
    //
    // Phần chữ hiển thị ở đây là HÀM chứ không phải chuỗi cố định, vì cùng một key "custom" có thể là một
    // model 3D, một ảnh 360° hay một tấm ảnh phẳng — thao tác chuột của ba thứ đó khác hẳn nhau nên câu
    // hướng dẫn cũng phải khác.
    'custom': {
        label: 'Bối cảnh tự tải lên',
        pointNoun: (scene) => (scene && scene.isSequential ? 'bước' : 'điểm tình huống'),
        loadingText: (scene) => ({
            'glb': 'Đang tải model 3D...',
            'pano-image': 'Đang tải ảnh 360°...',
            'pano-video': 'Đang tải video 360°...',
            'image-2d': 'Đang tải ảnh bối cảnh...',
        }[scene && scene.assetType] ?? 'Đang tải bối cảnh...'),
        hintHtml: (scene) => ({
            'glb': 'Kéo để xoay · lăn chuột để phóng to · bấm dấu <b>!</b> để xử lý tình huống',
            'pano-image': 'Kéo để nhìn quanh · lăn chuột để phóng to · bấm dấu <b>!</b> để xử lý tình huống',
            'pano-video': 'Kéo để nhìn quanh · lăn chuột để phóng to · bấm dấu <b>!</b> để xử lý tình huống',
            'image-2d': 'Kéo để di chuyển ảnh · lăn chuột để phóng to · bấm dấu <b>!</b> để xử lý tình huống',
        }[scene && scene.assetType] ?? 'Bấm dấu <b>!</b> để xử lý tình huống'),
        reference: null,
        async load() {
            const module = await import('/js/sim-asset-scene.js');
            return module.createAssetWorld;
        },
    },
};

/** Tra bối cảnh theo key; key lạ (dữ liệu cũ, gõ sai) rơi về cảnh công trường thay vì làm hỏng cả trang. */
export function getSimScene(sceneKey) {
    return SIM_SCENE_MAP[sceneKey] ?? SIM_SCENE_MAP['construction'];
}
