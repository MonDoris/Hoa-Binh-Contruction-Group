// File: API/Controllers/ScenarioController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Scenario. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IScenarioService"/>.</summary>
public class ScenarioController : BaseApiController
{
    private readonly IScenarioService _service;

    public ScenarioController(IScenarioService service) => _service = service;

    [HttpGet("by-course/{courseId:int}")]
    public async Task<ActionResult<IEnumerable<ScenarioSceneListDto>>> GetByCourse(int courseId)
        => ToActionResult(await _service.GetByCourse(courseId));

    [HttpGet("{id:int}/manage")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<ScenarioManageDto>> GetForManage(int id)
        => ToActionResult(await _service.GetForManage(id));

    [HttpPost]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Create(ScenarioSceneRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Update(int id, ScenarioSceneRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));

    [HttpPost("{sceneId:int}/node")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> AddNode(int sceneId, ScenarioNodeRequest request)
        => ToActionResult(await _service.AddNode(sceneId, request));

    [HttpPut("node/{nodeId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> UpdateNode(int nodeId, ScenarioNodeRequest request)
        => ToActionResult(await _service.UpdateNode(nodeId, request));

    [HttpDelete("node/{nodeId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> DeleteNode(int nodeId)
        => ToActionResult(await _service.DeleteNode(nodeId));

    [HttpPut("{sceneId:int}/character-scale")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> SetCharacterScale(int sceneId, [FromBody] float value)
        => ToActionResult(await _service.SetCharacterScale(sceneId, value));

    [HttpPost("{sceneId:int}/character")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<ScenarioCharacterDto>> AddCharacter(int sceneId, ScenarioCharacterRequest request)
        => ToActionResult(await _service.AddCharacter(sceneId, request));

    [HttpPut("character/{characterId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<ScenarioCharacterDto>> UpdateCharacter(int characterId, ScenarioCharacterRequest request)
        => ToActionResult(await _service.UpdateCharacter(characterId, request));

    [HttpDelete("character/{characterId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> DeleteCharacter(int characterId)
        => ToActionResult(await _service.DeleteCharacter(characterId));

    [HttpPost("{sceneId:int}/start")]
    public async Task<ActionResult<ScenarioStartDto>> Start(int sceneId, [FromQuery] int enrollmentId)
        => ToActionResult(await _service.Start(sceneId, enrollmentId));

    [HttpPost("attempt/{attemptId:int}/answer")]
    public async Task<ActionResult<ScenarioAnswerResultDto>> Answer(int attemptId, SubmitScenarioAnswerRequest request)
        => ToActionResult(await _service.Answer(attemptId, request));

    [HttpPost("attempt/{attemptId:int}/finish")]
    public async Task<ActionResult<ScenarioResultDto>> Finish(int attemptId)
        => ToActionResult(await _service.Finish(attemptId));

    [HttpGet("attempt/{attemptId:int}/result")]
    public async Task<ActionResult<ScenarioResultDto>> GetResult(int attemptId)
        => ToActionResult(await _service.GetResult(attemptId));

    [HttpGet("{sceneId:int}/my-attempt")]
    public async Task<ActionResult<IEnumerable<object>>> GetMyAttempt(int sceneId, [FromQuery] int enrollmentId)
        => ToActionResult(await _service.GetMyAttempt(sceneId, enrollmentId));
}
