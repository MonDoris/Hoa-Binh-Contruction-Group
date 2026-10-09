// File: API/GlobalUsings.cs
// API là lớp ngoài cùng: thấy Domain và Application. Infrastructure thì KHÔNG — xem ghi chú bên dưới.

// ----- Domain -----
global using HbELearning.Domain.Common;
global using HbELearning.Domain.Constants;
global using HbELearning.Domain.Entities;
global using HbELearning.Domain.Enums;

// ----- Application -----
global using HbELearning.Application;
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
global using HbELearning.Application.Features.ExternalCertificates;
global using HbELearning.Application.Features.Scorm;
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

// ----- Infrastructure: CỐ Ý KHÔNG có ở đây -----
// Controller và Hub không được nhìn thấy Infrastructure. Chỉ Program.cs (nơi lắp ráp) mới khai báo
// using tới nó, ngay trong tệp Program.cs. Gõ AppDbContext trong một Controller sẽ không biên dịch được.

// ----- Chính lớp API -----
global using HbELearning.API.Authorization;
global using HbELearning.API.Hubs;
global using HbELearning.API.Extensions;
global using HbELearning.API.Services;
