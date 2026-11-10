// File: wwwroot/pages/sim-library/sim-library.js
import { createAssetWorld } from '/js/sim/sim-asset-scene.js';

(async function () {
    await Lang.ready;
    if (!Auth.require(['Admin', 'Trưởng Nhóm', 'Giảng Viên'], 'sim-library.view')) return;

    mountLayout('sim-library', 'Thư viện 3D',
        'Kho bối cảnh dùng chung cho mọi mô phỏng: các không gian dựng sẵn của hệ thống, cùng model 3D và '
        + 'ảnh/video 360° tự tải lên — thêm một lần, mọi khóa học chọn lại');

    const canCreate = Auth.can('sim-library.create') !== false;
    const canUpdate = Auth.can('sim-library.update') !== false;
    const canDelete = Auth.can('sim-library.delete') !== false;

    // 'builtin' KHÔNG phải một loại tệp mà là bối cảnh dựng sẵn bằng code (xem js/sim/sim-registry.js) — phải
    // khớp hằng BuiltinAssetType trong Controllers/SimAssetController.cs.
    const TYPE_LABEL = {
        'builtin': 'Bối cảnh dựng sẵn',
        'glb': 'Model 3D',
        'pano-image': 'Ảnh 360°',
        'pano-video': 'Video 360°',
        'image-2d': 'Ảnh thường',
        'character': 'Nhân vật',
    };
    const TYPE_ICON = {
        'builtin': 'ph-buildings',
        'glb': 'ph-cube',
        'pano-image': 'ph-panorama',
        'pano-video': 'ph-video',
        'image-2d': 'ph-image',
        'character': 'ph-person',
    };
    /** Đuôi tệp chấp nhận theo loại — phải khớp ExtensionByAssetType trong Controllers/SimAssetController.cs. */
    const TYPE_ACCEPT = {
        'glb': '.glb,.gltf,.fbx,.obj',
        'pano-image': '.jpg,.jpeg,.png,.webp',
        'pano-video': '.mp4,.webm',
        'image-2d': '.jpg,.jpeg,.png,.webp,.gif',
        'character': '.glb,.gltf',
    };
    const TYPE_HINT = {
        'glb': 'Nên dùng .glb — gọn nhất và mang sẵn vật liệu, hoạt cảnh. Tệp .fbx/.obj vẫn mở được nhưng .obj tách rời sẽ mất màu vật liệu.',
        'pano-image': 'Ảnh panorama 360° dạng equirectangular (tỉ lệ 2:1), chụp bằng chế độ 360 của điện thoại hoặc máy Insta360.',
        'pano-video': 'Video quay 360°. Tệp nặng nên cân nhắc nén trước khi tải lên.',
        'image-2d': 'Ảnh chụp thiết bị hoặc sơ đồ mặt bằng — học viên xem phẳng, kéo và phóng to.',
        'character': 'Model NGƯỜI có xương, dùng để đặt vào cảnh chứ không phải làm bối cảnh. Chỉ nhận .glb/.gltf '
            + 'vì trình dựng cần đọc được bộ xương và các đoạn animation trong tệp. Nguồn quen dùng: mixamo.com '
            + '(miễn phí, không đòi ghi công) — tải nhân vật kèm các tư thế rồi gộp thành một tệp, xem '
            + 'wwwroot/models/README.txt. Tệp không có đoạn animation nào thì nhân vật vẫn đứng được trong cảnh, '
            + 'chỉ là không diễn tư thế.',
    };

    if (!canCreate) {
        document.getElementById('btn-add').classList.add('hidden');
        document.getElementById('btn-add-character').classList.add('hidden');
    }

    document.getElementById('btn-search').addEventListener('click', loadList);
    document.getElementById('f-type').addEventListener('change', loadList);
    document.getElementById('f-keyword').addEventListener('keydown', e => { if (e.key === 'Enter') loadList(); });
    document.getElementById('btn-add').addEventListener('click', () => openUploadForm());
    document.getElementById('btn-add-character')?.addEventListener('click', () => openUploadForm(null, 'character'));

    await loadList();

    // ==================== DANH SÁCH ====================
    async function loadList() {
        const assetType = document.getElementById('f-type').value;
        const keyword = document.getElementById('f-keyword').value.trim();
        const query = new URLSearchParams();
        if (assetType) query.set('assetType', assetType);
        if (keyword) query.set('keyword', keyword);

        let itemList;
        try { itemList = await API.get(`/simasset?${query}`); }
        catch (ex) { toastErr(ex.message); return; }

        const box = document.getElementById('lib-list');
        if (!itemList.length) {
            box.innerHTML = `<div class="card"><div class="card-body empty">
                <i class="ph ph-cube" style="font-size:34px"></i>
                <p>Thư viện chưa có mục nào${keyword || assetType ? ' khớp điều kiện lọc' : ''}.</p>
                ${canCreate ? '<p class="small muted">Bấm "Thêm tệp vào thư viện" để tải model 3D hoặc ảnh 360° lên dùng chung cho mọi mô phỏng.</p>' : ''}
            </div></div>`;
            return;
        }

        box.innerHTML = `<div class="lib-grid">` + itemList.map(item => `
            <div class="lib-card">
                <div class="lib-thumb">
                    ${item.thumbnailUrl
                        ? `<img src="${esc(item.thumbnailUrl)}" alt="${esc(item.name)}">`
                        : `<i class="ph ${TYPE_ICON[item.assetType] || 'ph-file'}"></i>`}
                    <span class="lib-type">${esc(TYPE_LABEL[item.assetType] || item.assetType)}</span>
                    ${item.usedByScenarioCount > 0
                        ? `<span class="lib-used" title="Số mô phỏng đang dùng bối cảnh này">${item.usedByScenarioCount} mô phỏng</span>` : ''}
                </div>
                <div class="lib-body">
                    <h4>${esc(item.name)}</h4>
                    <div class="meta">
                        ${item.category ? `<i class="ph ph-tag"></i> ${esc(item.category)}<br>` : ''}
                        ${isBuiltin(item)
                            ? 'Dựng sẵn trong hệ thống — luôn dùng được, không cần tải tệp'
                            : `${formatSize(item.fileSizeByte)}${item.createdByName ? ` · ${esc(item.createdByName)}` : ''}`}
                    </div>
                </div>
                <div class="lib-foot">
                    <button class="btn ghost sm" data-preview="${itemKey(item)}"><i class="ph ph-eye"></i> Xem</button>
                    ${canUpdate ? `<button class="btn ghost sm" data-edit="${itemKey(item)}" title="Sửa tên / mô tả"><i class="ph ph-pencil-simple"></i></button>` : ''}
                    ${canDelete && !isBuiltin(item)
                        ? `<button class="btn ghost sm" data-del="${itemKey(item)}" title="Xóa khỏi thư viện"><i class="ph ph-trash"></i></button>` : ''}
                </div>
            </div>`).join('') + `</div>`;

        // Khóa tra cứu phải kèm loại chứ không dùng thẳng Id: bối cảnh dựng sẵn và tệp tải lên nằm ở hai bảng
        // khác nhau nên Id của chúng trùng số như thường — dùng Id trần thì bấm "Xem" ở thẻ này lại mở thẻ kia.
        const itemByKey = new Map(itemList.map(x => [itemKey(x), x]));
        box.querySelectorAll('[data-preview]').forEach(b =>
            b.addEventListener('click', () => openPreview(itemByKey.get(b.dataset.preview))));
        box.querySelectorAll('[data-edit]').forEach(b =>
            b.addEventListener('click', () => openUploadForm(itemByKey.get(b.dataset.edit))));
        box.querySelectorAll('[data-del]').forEach(b =>
            b.addEventListener('click', () => deleteAsset(itemByKey.get(b.dataset.del))));
    }

    /** Bối cảnh dựng sẵn là mục CÓ sceneKey — nó trỏ tới một hàm dựng cảnh trong js/sim/sim-registry.js chứ không
     *  tới một tệp trong wwwroot/uploads. */
    function isBuiltin(item) {
        return !!item?.sceneKey;
    }

    function itemKey(item) {
        return `${isBuiltin(item) ? 'builtin' : 'asset'}:${item.id}`;
    }

    function formatSize(byteCount) {
        if (!byteCount) return '';
        const mb = byteCount / 1024 / 1024;
        return mb >= 1 ? `${mb.toFixed(1)} MB` : `${Math.round(byteCount / 1024)} KB`;
    }

    async function deleteAsset(item) {
        if (!item) return;
        // Server cũng không có endpoint xóa bối cảnh dựng sẵn; chặn ở đây chỉ để nói rõ LÝ DO thay vì để
        // người dùng nhận một lỗi 404 khó hiểu.
        if (isBuiltin(item)) {
            toastErr('Bối cảnh dựng sẵn không xóa được — không gian này do mã nguồn dựng nên, xóa bản ghi cũng '
                + 'không xóa được cảnh mà chỉ làm nó biến mất khỏi danh sách chọn.');
            return;
        }
        if (!await confirmDialog(`Xóa "${item.name}" khỏi thư viện? Tệp sẽ bị xóa khỏi máy chủ.`)) return;
        try {
            toastOk((await API.del(`/simasset/${item.id}`)).message);
            await loadList();
        } catch (ex) { toastErr(ex.message); }
    }

    // ==================== TẢI LÊN / SỬA ====================
    let selectedFile = null;
    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('u-file');

    /** @param {string} [defaultType] loại chọn sẵn khi thêm mới — nút "Thêm nhân vật" truyền 'character'. */
    function openUploadForm(item, defaultType) {
        // Sửa thì KHÔNG cho thay tệp: mô phỏng đang dùng tệp này trỏ theo đường dẫn, thay tệp tại chỗ sẽ
        // âm thầm đổi bối cảnh của những mô phỏng đó — muốn đổi tệp thì tải lên một mục mới rồi chọn lại.
        const isEdit = !!item;
        const builtin = isBuiltin(item);

        document.getElementById('upload-title').textContent = builtin ? 'Sửa thông tin bối cảnh dựng sẵn'
            : isEdit ? 'Sửa thông tin tệp'
            : defaultType === 'character' ? 'Thêm nhân vật vào thư viện' : 'Thêm tệp vào thư viện';
        document.getElementById('u-id').value = item?.id || '';
        document.getElementById('u-scene-key').value = item?.sceneKey || '';
        document.getElementById('u-name').value = item?.name || '';
        document.getElementById('u-category').value = item?.category || '';
        document.getElementById('u-description').value = item?.description || '';

        // Bối cảnh dựng sẵn không có loại tệp lẫn tệp đính kèm, nên ẩn hẳn hai phần đó thay vì hiện ra rồi
        // khóa lại — một ô "Loại bối cảnh" trống và một khung kéo thả vô tác dụng chỉ làm người dùng phân vân.
        document.getElementById('u-builtin-note').classList.toggle('hidden', !builtin);
        document.getElementById('u-type-field').classList.toggle('hidden', builtin);
        dropZone.classList.toggle('hidden', isEdit);

        if (!builtin) {
            document.getElementById('u-type').value = item?.assetType || defaultType || 'glb';
            document.getElementById('u-type').disabled = isEdit;
            updateTypeHint();
        }

        selectedFile = null;
        fileInput.value = '';
        dropZone.classList.remove('has-file');
        document.getElementById('drop-label').textContent = 'Kéo thả tệp vào đây hoặc bấm để chọn';
        openModal('modal-upload');
    }

    function updateTypeHint() {
        const type = document.getElementById('u-type').value;
        document.getElementById('u-type-hint').textContent = TYPE_HINT[type] || '';
        document.getElementById('drop-note').textContent = `Chấp nhận: ${TYPE_ACCEPT[type]} — tối đa 200 MB`;
        fileInput.accept = TYPE_ACCEPT[type];
    }

    document.getElementById('u-type').addEventListener('change', () => {
        // Đổi loại sau khi đã chọn tệp thì tệp cũ gần như chắc chắn sai đuôi — bỏ chọn để người dùng chọn lại.
        selectedFile = null;
        fileInput.value = '';
        dropZone.classList.remove('has-file');
        document.getElementById('drop-label').textContent = 'Kéo thả tệp vào đây hoặc bấm để chọn';
        updateTypeHint();
    });

    attachDropZone(dropZone, fileInput, pickFile);   // js/app.js

    function pickFile(file) {
        selectedFile = file;
        dropZone.classList.add('has-file');
        document.getElementById('drop-label').innerHTML = `<b>${esc(file.name)}</b> · ${formatSize(file.size)}`;
        // Tên tệp gốc là gợi ý tên thư viện tốt hơn hẳn ô trống — người dùng sửa lại nhanh hơn là gõ từ đầu.
        const nameBox = document.getElementById('u-name');
        if (!nameBox.value.trim()) nameBox.value = file.name.replace(/\.[^.]+$/, '');
    }

    document.getElementById('btn-save-upload').addEventListener('click', async () => {
        const id = document.getElementById('u-id').value;
        const sceneKey = document.getElementById('u-scene-key').value;
        const name = document.getElementById('u-name').value.trim();
        if (!name) { toastErr('Vui lòng đặt tên cho mục này trong thư viện.'); return; }

        const description = document.getElementById('u-description').value.trim();
        const category = document.getElementById('u-category').value.trim();

        try {
            if (id && sceneKey) {
                // Bối cảnh dựng sẵn nằm ở bảng khác nên có đường dẫn riêng — xem SimAssetController.UpdateBuiltin.
                toastOk((await API.put(`/simasset/builtin/${id}`, { name, description, category })).message);
            } else if (id) {
                toastOk((await API.put(`/simasset/${id}`, { name, description, category })).message);
            } else {
                if (!selectedFile) { toastErr('Chưa chọn tệp.'); return; }
                const form = new FormData();
                form.append('file', selectedFile);
                form.append('name', name);
                form.append('assetType', document.getElementById('u-type').value);
                form.append('description', description);
                form.append('category', category);
                await API.postForm('/simasset', form);
                toastOk('Đã thêm tệp vào thư viện. Mở "Xem" rồi bấm "Đặt làm ảnh xem trước" để thư viện dễ nhìn hơn.');
            }
            closeModal('modal-upload');
            await loadList();
        } catch (ex) { toastErr(ex.message); }
    });

    // ==================== XEM TRƯỚC 3D ====================
    let previewWorld = null;
    let previewItem = null;

    async function openPreview(item) {
        if (!item) return;
        previewItem = item;
        document.getElementById('preview-title').textContent = item.name;
        document.getElementById('preview-stat').textContent = '';
        document.getElementById('preview-stat').classList.remove('warn');

        const stageEl = document.getElementById('preview-stage');
        const loadingEl = document.getElementById('preview-loading');
        const progressEl = document.getElementById('preview-progress');
        const progressBarEl = document.getElementById('preview-progress-bar');
        loadingEl.style.display = '';
        progressEl.classList.add('hidden');
        progressBarEl.style.width = '0%';
        openModal('modal-preview');

        const onProgress = (text, percent) => {
            document.getElementById('preview-loading-text').textContent = text;
            if (typeof percent === 'number') {
                progressEl.classList.remove('hidden');
                progressBarEl.style.width = `${percent}%`;
            }
        };

        try {
            if (isBuiltin(item)) {
                // Dựng bằng ĐÚNG hàm mà trang học viên sẽ gọi (js/sim/sim-registry.js), không phải một bản xem
                // trước rút gọn — xem trước bằng thứ khác thì không kiểm chứng được cảnh thật trông thế nào.
                const { getSimScene } = await import('/js/sim/sim-registry.js');
                const sceneDef = getSimScene(item.sceneKey);
                onProgress(typeof sceneDef.loadingText === 'function' ? sceneDef.loadingText(null) : sceneDef.loadingText);
                const createWorld = await sceneDef.load();
                previewWorld = await createWorld(stageEl, onProgress);
            } else {
                // Nhân vật cũng là model 3D nên xem trước bằng đúng đường của 'glb' — createAssetWorld chỉ
                // phân biệt model / ảnh 360° / ảnh phẳng, không biết tới khái niệm nhân vật.
                previewWorld = await createAssetWorld(stageEl, onProgress,
                    { assetUrl: item.fileUrl, assetType: item.assetType === 'character' ? 'glb' : item.assetType });
            }
        } catch (ex) {
            document.getElementById('preview-loading-text').textContent = isBuiltin(item)
                ? 'Không dựng được bối cảnh này: ' + (ex?.message || 'lỗi khi nạp mã dựng cảnh.')
                : 'Không mở được tệp này: ' + (ex?.message || 'định dạng không đọc được.');
            progressEl.classList.add('hidden');
            console.error(ex);
            return;
        }

        loadingEl.style.display = 'none';
        setupPreviewTool();
        showStat();
    }

    /** Số tam giác của model — con số quyết định máy cấu hình yếu của học viên có chạy nổi hay không. */
    function showStat() {
        const statEl = document.getElementById('preview-stat');
        const stat = previewWorld.getStat?.();
        if (!stat || stat.assetType !== 'glb') return;

        const heavy = stat.triangleCount > 800000;
        statEl.classList.toggle('warn', heavy);
        statEl.innerHTML = `<i class="ph ph-triangle"></i> ${stat.triangleCount.toLocaleString('vi-VN')} tam giác · ${stat.meshCount} khối`
            + (heavy ? ' — model khá nặng, máy cấu hình thấp của học viên có thể bị giật. Nên giảm lưới (decimate) trước khi dùng.' : '');
    }

    let previewToolReady = false;
    function setupPreviewTool() {
        const rotateBtn = document.getElementById('pv-rotate');
        rotateBtn.classList.toggle('hidden', !previewWorld.canAutoRotate);

        // Gắn sự kiện đúng MỘT lần cho cả trang: hộp thoại xem trước dùng lại chính các nút này cho mọi
        // tệp, gắn lại mỗi lần mở sẽ cộng dồn handler và một cú bấm chạy nhiều lần.
        if (previewToolReady) return;
        previewToolReady = true;

        document.getElementById('pv-reset').addEventListener('click', () => previewWorld?.resetCamera());

        rotateBtn.addEventListener('click', () => {
            const on = previewWorld.setAutoRotate(!previewWorld.isAutoRotating());
            rotateBtn.classList.toggle('active', on);
        });

        const brightnessLevelList = [1, 1.35, 1.75, 0.7];
        let brightnessIndex = 0;
        document.getElementById('pv-bright').addEventListener('click', () => {
            brightnessIndex = (brightnessIndex + 1) % brightnessLevelList.length;
            previewWorld?.setBrightness(brightnessLevelList[brightnessIndex]);
        });

        document.getElementById('pv-fullscreen').addEventListener('click', () => {
            if (document.fullscreenElement) document.exitFullscreen();
            else document.getElementById('preview-stage').requestFullscreen?.().catch(() => {});
        });

        document.getElementById('pv-capture').addEventListener('click', async () => {
            if (!previewWorld || !previewItem) return;
            try {
                const path = isBuiltin(previewItem)
                    ? `/simasset/builtin/${previewItem.id}/thumbnail`
                    : `/simasset/${previewItem.id}/thumbnail`;
                await API.post(path, { dataUrl: previewWorld.capture(640) });
                toastOk('Đã lưu ảnh xem trước từ góc nhìn hiện tại.');
                await loadList();
            } catch (ex) { toastErr(ex.message); }
        });

        document.getElementById('btn-close-preview').addEventListener('click', closePreview);
    }

    function closePreview() {
        // BẮT BUỘC dọn: mỗi lần mở là một ngữ cảnh WebGL mới, không giải phóng thì xem vài tệp là trình
        // duyệt đạt trần số ngữ cảnh và những lần mở sau chỉ ra màn hình đen.
        previewWorld?.dispose();
        previewWorld = null;
        previewItem = null;
        if (document.fullscreenElement) document.exitFullscreen();
        closeModal('modal-preview');
    }
})();
