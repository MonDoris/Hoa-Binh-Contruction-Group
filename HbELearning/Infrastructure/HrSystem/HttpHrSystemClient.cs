// File: Infrastructure/HrSystem/HttpHrSystemClient.cs
using System.Net.Http.Headers;
using System.Text.Json;

namespace HbELearning.Infrastructure.HrSystem;

/// <summary>
/// Đọc dữ liệu nhân sự từ một phần mềm quản lý nhân sự qua REST + API Key. Chưa biết trước phần mềm nào nên MỌI
/// thứ riêng của nó nằm trong appsettings.json (mục "HrSystem"), không nằm trong mã:
///   - BaseUrl, ApiKey: địa chỉ gốc và khóa. Thiếu một trong hai = chưa kết nối (IsConfigured = false).
///   - ApiKeyHeader / ApiKeyPrefix: khóa gửi trong header nào ("X-API-Key", hoặc "Authorization" kèm tiền tố "Bearer ").
///   - DepartmentPath / PositionPath / EmployeePath: đường dẫn từng danh sách (PositionPath, DepartmentPath bỏ trống = bỏ qua).
///   - ListProperty: tên thuộc tính chứa mảng nếu phản hồi bọc ngoài ({"data":[...]}); bỏ trống nếu phản hồi là mảng trần.
///   - PageParam / PageSizeParam / PageSize: nếu phần mềm phân trang. Bỏ trống PageParam = lấy một lần.
///   - Fields:*: tên thuộc tính JSON ứng với từng trường (hỗ trợ lối tắt lồng "department.code").
/// Khi có API Key thật chỉ cần điền cấu hình cho khớp tài liệu API của phần mềm đó; nếu nó có kiểu xác thực hay dữ
/// liệu khác hẳn (OAuth, SOAP...), viết một lớp IHrSystemClient mới thay cho lớp này.
/// </summary>
public class HttpHrSystemClient : IHrSystemClient
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpHrSystemClient> _logger;
    private readonly IConfigurationSection _section;

    private readonly string? _baseUrl;
    private readonly string? _apiKey;
    private readonly string _apiKeyHeader;
    private readonly string _apiKeyPrefix;
    private readonly string? _listProperty;
    private readonly string? _pageParam;
    private readonly string _pageSizeParam;
    private readonly int _pageSize;

    public string ProviderName => _section["Name"] is { Length: > 0 } name ? name : "Phần mềm nhân sự";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey)
        && Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public HttpHrSystemClient(HttpClient http, IConfiguration config, ILogger<HttpHrSystemClient> logger)
    {
        _http = http;
        _logger = logger;
        _section = config.GetSection("HrSystem");

        _baseUrl = _section["BaseUrl"];
        _apiKey = _section["ApiKey"];
        _apiKeyHeader = _section["ApiKeyHeader"] is { Length: > 0 } h ? h : "X-API-Key";
        _apiKeyPrefix = _section["ApiKeyPrefix"] ?? "";
        _listProperty = _section["ListProperty"];
        _pageParam = _section["PageParam"];
        _pageSizeParam = _section["PageSizeParam"] is { Length: > 0 } ps ? ps : "pageSize";
        _pageSize = int.TryParse(_section["PageSize"], out var size) && size > 0 ? size : 200;

        _http.Timeout = TimeSpan.FromSeconds(int.TryParse(_section["TimeoutSeconds"], out var t) && t > 0 ? t : 60);
        if (Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri))
            _http.BaseAddress = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public async Task<HrSnapshot> FetchAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) throw new HrSystemException("Chưa cấu hình kết nối phần mềm nhân sự.");

        var departmentList = (await ReadAllAsync(_section["DepartmentPath"], ct))
            .Select(e => new HrDepartmentRecord(Field(e, "Department", "Code", "code") ?? "", Field(e, "Department", "Name", "name") ?? ""))
            .ToList();

        var positionList = (await ReadAllAsync(_section["PositionPath"], ct))
            .Select(e => new HrPositionRecord(Field(e, "Position", "Code", "code") ?? "", Field(e, "Position", "Name", "name") ?? "",
                Field(e, "Position", "DepartmentCode", "departmentCode")))
            .ToList();

        var employeeList = (await ReadAllAsync(_section["EmployeePath"], ct))
            .Select(e => new HrEmployeeRecord(
                Field(e, "Employee", "Code", "employeeCode") ?? "",
                Field(e, "Employee", "FullName", "fullName") ?? "",
                Field(e, "Employee", "Email", "email"),
                Field(e, "Employee", "PhoneNumber", "phoneNumber"),
                Field(e, "Employee", "DepartmentCode", "departmentCode"),
                Field(e, "Employee", "PositionCode", "positionCode"),
                IsActive(e)))
            .ToList();

        if (employeeList.Count == 0)
            throw new HrSystemException("Phần mềm nhân sự không trả về nhân viên nào — kiểm tra EmployeePath và ListProperty trong cấu hình HrSystem.");

        return new HrSnapshot(departmentList, positionList, employeeList);
    }

    // ---------- Gọi API ----------

    private async Task<List<JsonElement>> ReadAllAsync(string? path, CancellationToken ct)
    {
        var result = new List<JsonElement>();
        if (string.IsNullOrWhiteSpace(path)) return result;

        for (var page = 1; page <= 1000; page++)
        {
            var url = path.TrimStart('/');
            if (!string.IsNullOrWhiteSpace(_pageParam))
                url += (url.Contains('?') ? "&" : "?") + $"{_pageParam}={page}&{_pageSizeParam}={_pageSize}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (_apiKeyHeader.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(_apiKeyPrefix + _apiKey);
            else
                request.Headers.TryAddWithoutValidation(_apiKeyHeader, _apiKeyPrefix + _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            JsonDocument doc;
            try
            {
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Phan mem nhan su tra loi {Status} khi goi {Path}", (int)response.StatusCode, path);
                    throw new HrSystemException(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                        ? "Phần mềm nhân sự từ chối API Key (401/403) — kiểm tra lại khóa và quyền của khóa."
                        : $"Phần mềm nhân sự trả về lỗi {(int)response.StatusCode} khi đọc {path}.");
                }
                doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            }
            catch (HrSystemException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogError(ex, "Khong doc duoc du lieu tu phan mem nhan su ({Path})", path);
                throw new HrSystemException("Không kết nối hoặc không đọc được dữ liệu từ phần mềm nhân sự — xem nhật ký máy chủ để biết chi tiết.", ex);
            }

            using (doc)
            {
                var array = string.IsNullOrWhiteSpace(_listProperty) ? doc.RootElement : Dig(doc.RootElement, _listProperty);
                if (array.ValueKind != JsonValueKind.Array)
                    throw new HrSystemException($"Phản hồi của {path} không phải danh sách — kiểm tra ListProperty trong cấu hình HrSystem.");

                var items = array.EnumerateArray().Select(x => x.Clone()).ToList();
                result.AddRange(items);
                if (string.IsNullOrWhiteSpace(_pageParam) || items.Count < _pageSize) break;
            }
        }
        return result;
    }

    // ---------- Đọc trường theo cấu hình ----------

    /// <summary>Giá trị chuỗi của một trường. Tên thuộc tính lấy từ Fields:{Group}:{Key}, mặc định là <paramref name="fallback"/>.</summary>
    private string? Field(JsonElement element, string group, string key, string fallback)
    {
        var name = _section[$"Fields:{group}:{key}"] is { Length: > 0 } configured ? configured : fallback;
        var value = Dig(element, name);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    /// <summary>Trường báo còn làm việc hay không. Không khai báo Fields:Employee:Active, hoặc nhân viên không có
    /// trường đó → coi là còn làm việc (an toàn: không vô hiệu hóa nhầm).</summary>
    private bool IsActive(JsonElement element)
    {
        var name = _section["Fields:Employee:Active"];
        if (string.IsNullOrWhiteSpace(name)) return true;

        var value = Dig(element, name);
        switch (value.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.String:
                var text = value.GetString()?.Trim().ToLowerInvariant();
                var inactiveText = _section["Fields:Employee:InactiveValues"] is { Length: > 0 } list
                    ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : new[] { "inactive", "resigned", "terminated", "nghỉ việc", "đã nghỉ" };
                return !inactiveText.Contains(text ?? "", StringComparer.OrdinalIgnoreCase);
            default: return true;
        }
    }

    /// <summary>Đi theo đường "a.b.c" qua các đối tượng lồng nhau; không có thì trả về phần tử kiểu Undefined.</summary>
    private static JsonElement Dig(JsonElement element, string path)
    {
        var current = element;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object) return default;
            var found = false;
            foreach (var property in current.EnumerateObject())
            {
                if (!property.Name.Equals(part, StringComparison.OrdinalIgnoreCase)) continue;
                current = property.Value;
                found = true;
                break;
            }
            if (!found) return default;
        }
        return current;
    }
}
