// File: Infrastructure/GlobalUsings.cs
// Infrastructure được nhìn thấy Application và Domain — đúng chiều cho phép.

// ----- Domain -----
global using HbELearning.Domain.Common;
global using HbELearning.Domain.Constants;
global using HbELearning.Domain.Entities;
global using HbELearning.Domain.Enums;

// ----- Application (các cổng mà lớp này hiện thực hóa) -----
global using HbELearning.Application.Common;
global using HbELearning.Application.Common.Interfaces;
global using HbELearning.Application.Common.Models;
global using HbELearning.Application.DTOs;
// DbSeeder nạp bảng điểm mặc định từ RankingEngine.DefaultPointTable — quy tắc nghiệp vụ
// nằm ở Application, Infrastructure đọc xuống là đúng chiều cho phép.
global using HbELearning.Application.Services;
global using HbELearning.Application.Features.HrSync;

// ----- Chính lớp Infrastructure -----
global using HbELearning.Infrastructure.Ai;
global using HbELearning.Infrastructure.BackgroundJobs;
global using HbELearning.Infrastructure.Certificates;
global using HbELearning.Infrastructure.Documents;
global using HbELearning.Infrastructure.Files;
global using HbELearning.Infrastructure.HrSystem;
global using HbELearning.Infrastructure.Identity;
global using HbELearning.Infrastructure.Import;
global using HbELearning.Infrastructure.Notifications;
global using HbELearning.Infrastructure.Persistence;
global using HbELearning.Infrastructure.Persistence.Seeding;

// ----- Nền tảng .NET -----
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
