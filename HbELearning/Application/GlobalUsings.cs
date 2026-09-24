// File: Application/GlobalUsings.cs
// Danh sách này CHÍNH LÀ bản khai những gì lớp Application được phép nhìn thấy: chỉ Domain và chính nó.
// Không có dòng nào trỏ tới HbELearning.Infrastructure, và sẽ không bao giờ được có.

// ----- Domain -----
global using HbELearning.Domain.Common;
global using HbELearning.Domain.Constants;
global using HbELearning.Domain.Entities;
global using HbELearning.Domain.Enums;

// ----- Chính lớp Application -----
global using HbELearning.Application.Common;
global using HbELearning.Application.Common.Interfaces;
global using HbELearning.Application.Common.Models;
global using HbELearning.Application.DTOs;
global using HbELearning.Application.Services;
global using HbELearning.Application.Common.Results;

// ----- Các ca sử dụng, mỗi chức năng một thư mục trong Application/Features -----
global using HbELearning.Application.Features.Ai;
global using HbELearning.Application.Features.AiDocuments;
global using HbELearning.Application.Features.AiQuickReplies;
global using HbELearning.Application.Features.AiTutor;
global using HbELearning.Application.Features.ApprovalDelegates;
global using HbELearning.Application.Features.ApprovalWorkflows;
global using HbELearning.Application.Features.Auth;
global using HbELearning.Application.Features.Certificates;
global using HbELearning.Application.Features.Chat;
global using HbELearning.Application.Features.Courses;
global using HbELearning.Application.Features.Dashboard;
global using HbELearning.Application.Features.Departments;
global using HbELearning.Application.Features.Enrollments;
global using HbELearning.Application.Features.Import;
global using HbELearning.Application.Features.LearningPaths;
global using HbELearning.Application.Features.LessonNotes;
global using HbELearning.Application.Features.Lessons;
global using HbELearning.Application.Features.Notifications;
global using HbELearning.Application.Features.OnlineClassRooms;
global using HbELearning.Application.Features.Positions;
global using HbELearning.Application.Features.Progress;
global using HbELearning.Application.Features.Quizzes;
global using HbELearning.Application.Features.Ranking;
global using HbELearning.Application.Features.Reports;
global using HbELearning.Application.Features.Roles;
global using HbELearning.Application.Features.Scenarios;
global using HbELearning.Application.Features.Shop;
global using HbELearning.Application.Features.SimAssets;
global using HbELearning.Application.Features.StudyReminders;
global using HbELearning.Application.Features.Translations;
global using HbELearning.Application.Features.Uploads;
global using HbELearning.Application.Features.Users;
global using HbELearning.Application.Features.VideoLearnings;

// ----- Nền tảng .NET — chỉ phần trừu tượng, KHÔNG có ASP.NET Core -----
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Caching.Memory;
