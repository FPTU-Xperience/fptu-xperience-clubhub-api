SET NOCOUNT ON;
USE [ClubReportHub_Club];
PRINT 'Seeding 73 FPT University clubs...';
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-SWO')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-SWO', N'CLB Street Workout', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FuStreetWorkout', N'fu-swo@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Street Workout', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FuStreetWorkout', [ContactEmail] = N'fu-swo@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-SWO';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-ISKT')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-ISKT', N'CLB iSkate', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTUISkate', N'fu-iskt@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB iSkate', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTUISkate', [ContactEmail] = N'fu-iskt@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-ISKT';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-VVIN')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-VVIN', N'CLB Vovinam', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fvchn', N'fu-vvin@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Vovinam', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fvchn', [ContactEmail] = N'fu-vvin@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-VVIN';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-FOOT')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-FOOT', N'Đội tuyển bóng đá', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fu.football.team', N'fu-foot@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Đội tuyển bóng đá', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fu.football.team', [ContactEmail] = N'fu-foot@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-FOOT';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-VOL')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-VOL', N'CLB Bóng chuyền', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FptVolleyballClub/', N'fu-vol@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Bóng chuyền', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FptVolleyballClub/', [ContactEmail] = N'fu-vol@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-VOL';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-BADM')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-BADM', N'CLB Cầu lông', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTUbadminton/', N'fu-badm@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Cầu lông', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTUbadminton/', [ContactEmail] = N'fu-badm@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-BADM';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-GYM')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-GYM', N'CLB Gym', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FuGymnastic', N'fu-gym@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Gym', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FuGymnastic', [ContactEmail] = N'fu-gym@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-GYM';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FPTU-FC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FPTU-FC', N'CLB Bóng đá Đại học FPT (FPTU.FC)', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTU.FC/', N'fptu-fc@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Bóng đá Đại học FPT (FPTU.FC)', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTU.FC/', [ContactEmail] = N'fptu-fc@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FPTU-FC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-TAEK')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-TAEK', N'CLB Taekwondo', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FTCTaekwondo/', N'fu-taek@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Taekwondo', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FTCTaekwondo/', [ContactEmail] = N'fu-taek@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-TAEK';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-NUNCH')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-NUNCH', N'CLB Côn nhị khúc', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/nunchaku.fnc/', N'fu-nunch@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Côn nhị khúc', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/nunchaku.fnc/', [ContactEmail] = N'fu-nunch@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-NUNCH';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-TTEN')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-TTEN', N'CLB Bóng bàn', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPT.Tabletennis/', N'fu-tten@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Bóng bàn', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPT.Tabletennis/', [ContactEmail] = N'fu-tten@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-TTEN';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-MUAY')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-MUAY', N'CLB Muay Thái', N'SPORTS', N'HAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fmuc.fptu/', N'fu-muay@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Muay Thái', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fmuc.fptu/', [ContactEmail] = N'fu-muay@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-MUAY';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FVC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FVC', N'FVC - FPT Vovinam Club HCM', N'SPORTS', N'HCM', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fvchcm/', N'hcm-fvc@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FVC - FPT Vovinam Club HCM', [Category] = N'SPORTS', [CampusCode] = N'HCM', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fvchcm/', [ContactEmail] = N'hcm-fvc@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FVC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FFC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FFC', N'FFC - Câu lạc bộ Bóng đá FPTU HCM', N'SPORTS', N'HCM', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FFC.FPTUHCM/', N'hcm-ffc@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FFC - Câu lạc bộ Bóng đá FPTU HCM', [Category] = N'SPORTS', [CampusCode] = N'HCM', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FFC.FPTUHCM/', [ContactEmail] = N'hcm-ffc@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FFC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FBC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FBC', N'FBC - FPTU HCM Basketball Club', N'SPORTS', N'HCM', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fptuhcmbasketballclub/', N'hcm-fbc@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FBC - FPTU HCM Basketball Club', [Category] = N'SPORTS', [CampusCode] = N'HCM', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fptuhcmbasketballclub/', [ContactEmail] = N'hcm-fbc@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FBC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FVB')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FVB', N'FVB - Câu lạc bộ Bóng chuyền FPTU HCM', N'SPORTS', N'HCM', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/volleyballfpt/', N'hcm-fvb@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FVB - Câu lạc bộ Bóng chuyền FPTU HCM', [Category] = N'SPORTS', [CampusCode] = N'HCM', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/volleyballfpt/', [ContactEmail] = N'hcm-fvb@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FVB';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'DN-FBC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'DN-FBC', N'FUDN Basketball Club', N'SPORTS', N'DAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/fudnbasketball', N'dn-fbc@fpt.edu.vn', N'02367300186', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FUDN Basketball Club', [Category] = N'SPORTS', [CampusCode] = N'DAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/fudnbasketball', [ContactEmail] = N'dn-fbc@fpt.edu.vn', [ContactPhone] = N'02367300186', [IsActive] = 1 WHERE [Code] = N'DN-FBC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'DN-FVC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'DN-FVC', N'FVC-ĐN - FU Vovinam Club Đà Nẵng', N'SPORTS', N'DAN', N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/fvcdn', N'dn-fvc@fpt.edu.vn', N'02367300186', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FVC-ĐN - FU Vovinam Club Đà Nẵng', [Category] = N'SPORTS', [CampusCode] = N'DAN', [Description] = N'Thể thao & võ thuật - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/fvcdn', [ContactEmail] = N'dn-fvc@fpt.edu.vn', [ContactPhone] = N'02367300186', [IsActive] = 1 WHERE [Code] = N'DN-FVC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-HEBE')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-HEBE', N'CLB Văn hóa nghệ thuật (Hebe)', N'ARTS', N'HAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/HebeFPT/', N'fu-hebe@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Văn hóa nghệ thuật (Hebe)', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/HebeFPT/', [ContactEmail] = N'fu-hebe@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-HEBE';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-MELD')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-MELD', N'CLB Melody', N'ARTS', N'HAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FptuMelodyClub', N'fu-meld@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Melody', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FptuMelodyClub', [ContactEmail] = N'fu-meld@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-MELD';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-GUIT')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-GUIT', N'CLB Guitar', N'ARTS', N'HAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fuguitarclub/', N'fu-guit@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Guitar', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fuguitarclub/', [ContactEmail] = N'fu-guit@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-GUIT';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-HIPH')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-HIPH', N'CLB Hiphop', N'ARTS', N'HAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/hiphopfptuniversity', N'fu-hiph@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Hiphop', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/hiphopfptuniversity', [ContactEmail] = N'fu-hiph@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-HIPH';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-YOSA')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-YOSA', N'CLB Yosakoi', N'ARTS', N'HAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTJuniorYosakoi', N'fu-yosa@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Yosakoi', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTJuniorYosakoi', [ContactEmail] = N'fu-yosa@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-YOSA';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-FTIC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-FTIC', N'CLB Nhạc cụ truyền thống (FTIC)', N'ARTS', N'HAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FTIC.FUHL', N'fu-ftic@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Nhạc cụ truyền thống (FTIC)', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FTIC.FUHL', [ContactEmail] = N'fu-ftic@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-FTIC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-CINE')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-CINE', N'CLB Điện ảnh', N'ARTS', N'HAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fucinemastudio/', N'fu-cine@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Điện ảnh', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fucinemastudio/', [ContactEmail] = N'fu-cine@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-CINE';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-SOLE')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-SOLE', N'Soleil Crew - CLB nhảy hiện đại', N'ARTS', N'HAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/soleilcrewfptu/', N'fu-sole@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Soleil Crew - CLB nhảy hiện đại', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/soleilcrewfptu/', [ContactEmail] = N'fu-sole@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-SOLE';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FTI')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FTI', N'FTI - FPT Traditional Instruments', N'ARTS', N'HCM', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/ftihcm/', N'hcm-fti@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FTI - FPT Traditional Instruments', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/ftihcm/', [ContactEmail] = N'hcm-fti@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FTI';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FBK')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FBK', N'FBK - FPT Beat King Club', N'ARTS', N'HCM', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FPTBeatKingClub/', N'hcm-fbk@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FBK - FPT Beat King Club', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FPTBeatKingClub/', [ContactEmail] = N'hcm-fbk@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FBK';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FSHP')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FSHP', N'F# - F# Live Music Club', N'ARTS', N'HCM', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FsharpLiveMusicClub/', N'hcm-fshp@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'F# - F# Live Music Club', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FsharpLiveMusicClub/', [ContactEmail] = N'hcm-fshp@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FSHP';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FSTY')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FSTY', N'FStyle - FStyle Crew', N'ARTS', N'HCM', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FStyleFamily/', N'hcm-fsty@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FStyle - FStyle Crew', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FStyleFamily/', [ContactEmail] = N'hcm-fsty@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FSTY';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-MEC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-MEC', N'MEC - Multimedia & Entertainment Club', N'ARTS', N'HCM', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/Mec.Fptuhcm/', N'hcm-mec@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'MEC - Multimedia & Entertainment Club', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/Mec.Fptuhcm/', [ContactEmail] = N'hcm-mec@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-MEC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FAC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FAC', N'FAC - FPT Art and Culture Community', N'ARTS', N'HCM', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fac.fptuhcm/', N'hcm-fac@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FAC - FPT Art and Culture Community', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fac.fptuhcm/', [ContactEmail] = N'hcm-fac@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FAC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-UDC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-UDC', N'UDC - Unity Dance Crew', N'ARTS', N'HCM', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/udcsg.fpt/', N'hcm-udc@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'UDC - Unity Dance Crew', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/udcsg.fpt/', [ContactEmail] = N'hcm-udc@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-UDC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'DN-DFP')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'DN-DFP', N'FU-Dancing Club (DfP - Dance for Passion)', N'ARTS', N'DAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/FUDancingClub/', N'dn-dfp@fpt.edu.vn', N'02367300186', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FU-Dancing Club (DfP - Dance for Passion)', [Category] = N'ARTS', [CampusCode] = N'DAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/FUDancingClub/', [ContactEmail] = N'dn-dfp@fpt.edu.vn', [ContactPhone] = N'02367300186', [IsActive] = 1 WHERE [Code] = N'DN-DFP';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'DN-NYS')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'DN-NYS', N'NYS Club - CLB Nghệ thuật FUDN', N'ARTS', N'DAN', N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/nysclubdn/', N'dn-nys@fpt.edu.vn', N'02367300186', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'NYS Club - CLB Nghệ thuật FUDN', [Category] = N'ARTS', [CampusCode] = N'DAN', [Description] = N'Nghệ thuật, âm nhạc & biểu diễn - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/nysclubdn/', [ContactEmail] = N'dn-nys@fpt.edu.vn', [ContactPhone] = N'02367300186', [IsActive] = 1 WHERE [Code] = N'DN-NYS';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'F-CODE')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'F-CODE', N'F-Code', N'TECHNOLOGY', N'GLOBAL', N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội & Hồ Chí Minh). Fanpage: https://www.facebook.com/fcodefpt/', N'f-code@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'F-Code', [Category] = N'TECHNOLOGY', [CampusCode] = N'GLOBAL', [Description] = N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội & Hồ Chí Minh). Fanpage: https://www.facebook.com/fcodefpt/', [ContactEmail] = N'f-code@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'F-CODE';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-JSCL')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-JSCL', N'CLB Kỹ sư phần mềm Nhật Bản (JS Club)', N'TECHNOLOGY', N'HAN', N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fu.jsclub', N'fu-jscl@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Kỹ sư phần mềm Nhật Bản (JS Club)', [Category] = N'TECHNOLOGY', [CampusCode] = N'HAN', [Description] = N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fu.jsclub', [ContactEmail] = N'fu-jscl@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-JSCL';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-AICL')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-AICL', N'CLB AI', N'TECHNOLOGY', N'HAN', N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/aiclub.fptu/', N'fu-aicl@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB AI', [Category] = N'TECHNOLOGY', [CampusCode] = N'HAN', [Description] = N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/aiclub.fptu/', [ContactEmail] = N'fu-aicl@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-AICL';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FIA')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FIA', N'FIA - FPT Information Assurance Club', N'TECHNOLOGY', N'HCM', N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fptinformationassurance/', N'hcm-fia@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FIA - FPT Information Assurance Club', [Category] = N'TECHNOLOGY', [CampusCode] = N'HCM', [Description] = N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fptinformationassurance/', [ContactEmail] = N'hcm-fia@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FIA';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-HNF')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-HNF', N'HNF - Hardware Network FPTU', N'TECHNOLOGY', N'HCM', N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/hnffptu/', N'hcm-hnf@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'HNF - Hardware Network FPTU', [Category] = N'TECHNOLOGY', [CampusCode] = N'HCM', [Description] = N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/hnffptu/', [ContactEmail] = N'hcm-hnf@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-HNF';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'DN-DEVR')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'DN-DEVR', N'FU-Dever - CLB Lập trình', N'TECHNOLOGY', N'DAN', N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/FPTUDever/', N'dn-devr@fpt.edu.vn', N'02367300186', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FU-Dever - CLB Lập trình', [Category] = N'TECHNOLOGY', [CampusCode] = N'DAN', [Description] = N'Công nghệ & kỹ thuật - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/FPTUDever/', [ContactEmail] = N'dn-devr@fpt.edu.vn', [ContactPhone] = N'02367300186', [IsActive] = 1 WHERE [Code] = N'DN-DEVR';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-DESG')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-DESG', N'CLB Design', N'ARTS', N'HAN', N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fudesigners', N'fu-desg@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Design', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fudesigners', [ContactEmail] = N'fu-desg@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-DESG';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-PHOT')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-PHOT', N'CLB Photography', N'ARTS', N'HAN', N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FUphotography.club', N'fu-phot@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Photography', [Category] = N'ARTS', [CampusCode] = N'HAN', [Description] = N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FUphotography.club', [ContactEmail] = N'fu-phot@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-PHOT';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FEV')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FEV', N'FEV - FPT Event Club', N'ARTS', N'HCM', N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FPTEventClub/', N'hcm-fev@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FEV - FPT Event Club', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FPTEventClub/', [ContactEmail] = N'hcm-fev@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FEV';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-CSG')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-CSG', N'CSG - CLB Truyền thông Cóc Sài Gòn', N'ARTS', N'HCM', N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/cocsaigonfuhcm/', N'hcm-csg@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CSG - CLB Truyền thông Cóc Sài Gòn', [Category] = N'ARTS', [CampusCode] = N'HCM', [Description] = N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/cocsaigonfuhcm/', [ContactEmail] = N'hcm-csg@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-CSG';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'DN-FUM')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'DN-FUM', N'FUM - FPT University Media', N'ARTS', N'DAN', N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/FUMedia/', N'dn-fum@fpt.edu.vn', N'02367300186', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FUM - FPT University Media', [Category] = N'ARTS', [CampusCode] = N'DAN', [Description] = N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/FUMedia/', [ContactEmail] = N'dn-fum@fpt.edu.vn', [ContactPhone] = N'02367300186', [IsActive] = 1 WHERE [Code] = N'DN-FUM';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'DN-EVO')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'DN-EVO', N'EVo - CLB Tổ chức sự kiện', N'ARTS', N'DAN', N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/FPTUDNEventClub/', N'dn-evo@fpt.edu.vn', N'02367300186', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'EVo - CLB Tổ chức sự kiện', [Category] = N'ARTS', [CampusCode] = N'DAN', [Description] = N'Truyền thông, thiết kế & sự kiện - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/FPTUDNEventClub/', [ContactEmail] = N'dn-evo@fpt.edu.vn', [ContactPhone] = N'02367300186', [IsActive] = 1 WHERE [Code] = N'DN-EVO';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-CHIN')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-CHIN', N'CLB Tiếng Trung', N'ACADEMIC', N'HAN', N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/tiengtrungFPT/', N'fu-chin@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Tiếng Trung', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/tiengtrungFPT/', [ContactEmail] = N'fu-chin@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-CHIN';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-MAYM')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-MAYM', N'Mây Mưa Club - CLB yêu thích Nhật Bản', N'ACADEMIC', N'HAN', N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/maymuaclub/', N'fu-maym@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Mây Mưa Club - CLB yêu thích Nhật Bản', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/maymuaclub/', [ContactEmail] = N'fu-maym@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-MAYM';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-ENGC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-ENGC', N'CLB Tiếng Anh', N'ACADEMIC', N'HAN', N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/englishclub.fu/', N'fu-engc@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Tiếng Anh', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/englishclub.fu/', [ContactEmail] = N'fu-engc@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-ENGC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-DANG')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-DANG', N'CLB Dango (anime, manga, tokusatsu...)', N'ACADEMIC', N'HAN', N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/groups/dango.clb/', N'fu-dang@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Dango (anime, manga, tokusatsu...)', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/groups/dango.clb/', [ContactEmail] = N'fu-dang@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-DANG';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-JSC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-JSC', N'JSC - CLB Phong Cách Nhật Bản', N'ACADEMIC', N'HCM', N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/clbJSC/', N'hcm-jsc@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'JSC - CLB Phong Cách Nhật Bản', [Category] = N'ACADEMIC', [CampusCode] = N'HCM', [Description] = N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/clbJSC/', [ContactEmail] = N'hcm-jsc@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-JSC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'DN-EFLM')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'DN-EFLM', N'Eflame - Câu lạc bộ Tiếng Anh', N'ACADEMIC', N'DAN', N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/EngCF/', N'dn-eflm@fpt.edu.vn', N'02367300186', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Eflame - Câu lạc bộ Tiếng Anh', [Category] = N'ACADEMIC', [CampusCode] = N'DAN', [Description] = N'Ngôn ngữ & văn hóa quốc tế - CLB sinh viên Trường Đại học FPT (Đà Nẵng). Fanpage: https://www.facebook.com/EngCF/', [ContactEmail] = N'dn-eflm@fpt.edu.vn', [ContactPhone] = N'02367300186', [IsActive] = 1 WHERE [Code] = N'DN-EFLM';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-BUSN')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-BUSN', N'CLB Business', N'ACADEMIC', N'HAN', N'Kinh doanh, tài chính & nghề nghiệp - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FU.Business/', N'fu-busn@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Business', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Kinh doanh, tài chính & nghề nghiệp - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FU.Business/', [ContactEmail] = N'fu-busn@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-BUSN';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-FSIC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-FSIC', N'CLB Chứng khoán (FSIC)', N'ACADEMIC', N'HAN', N'Kinh doanh, tài chính & nghề nghiệp - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FSIC-FPT-Securities-Investment-Cl', N'fu-fsic@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Chứng khoán (FSIC)', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Kinh doanh, tài chính & nghề nghiệp - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FSIC-FPT-Securities-Investment-Cl', [ContactEmail] = N'fu-fsic@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-FSIC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-BEC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-BEC', N'BEC - Business Economics Club', N'ACADEMIC', N'HCM', N'Kinh doanh, tài chính & nghề nghiệp - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/bec.fptuhcm/', N'hcm-bec@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'BEC - Business Economics Club', [Category] = N'ACADEMIC', [CampusCode] = N'HCM', [Description] = N'Kinh doanh, tài chính & nghề nghiệp - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/bec.fptuhcm/', [ContactEmail] = N'hcm-bec@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-BEC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-SKIL')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-SKIL', N'Skillcetera', N'ACADEMIC', N'HCM', N'Kinh doanh, tài chính & nghề nghiệp - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/skillcetera/', N'hcm-skil@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Skillcetera', [Category] = N'ACADEMIC', [CampusCode] = N'HCM', [Description] = N'Kinh doanh, tài chính & nghề nghiệp - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/skillcetera/', [ContactEmail] = N'hcm-skil@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-SKIL';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-NOSH')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-NOSH', N'CLB No Shy', N'ACADEMIC', N'HAN', N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/noshyclub/', N'fu-nosh@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB No Shy', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/noshyclub/', [ContactEmail] = N'fu-nosh@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-NOSH';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-BOOK')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-BOOK', N'CLB Sách', N'ACADEMIC', N'HAN', N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fu.bukclub/', N'fu-book@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Sách', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fu.bukclub/', [ContactEmail] = N'fu-book@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-BOOK';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-DEBT')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-DEBT', N'FU Debate Club - CLB Tranh biện', N'ACADEMIC', N'HAN', N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FUDebateClub/', N'fu-debt@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FU Debate Club - CLB Tranh biện', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FUDebateClub/', [ContactEmail] = N'fu-debt@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-DEBT';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-PSYC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-PSYC', N'CLB Tâm lý học', N'ACADEMIC', N'HAN', N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPT.Psy/', N'fu-psyc@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Tâm lý học', [Category] = N'ACADEMIC', [CampusCode] = N'HAN', [Description] = N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPT.Psy/', [ContactEmail] = N'fu-psyc@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-PSYC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FPS')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FPS', N'FPS - FPTU Public Speaking', N'ACADEMIC', N'HCM', N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FPTUPublicSpeakingClub/', N'hcm-fps@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FPS - FPTU Public Speaking', [Category] = N'ACADEMIC', [CampusCode] = N'HCM', [Description] = N'Học thuật, kỹ năng & phát triển cá nhân - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FPTUPublicSpeakingClub/', [ContactEmail] = N'hcm-fps@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FPS';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-ESPT')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-ESPT', N'CLB Esports', N'SPORTS', N'HAN', N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/ESCFUHL', N'fu-espt@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Esports', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/ESCFUHL', [ContactEmail] = N'fu-espt@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-ESPT';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-GOCL')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-GOCL', N'CLB Cờ vây', N'SPORTS', N'HAN', N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fptgoclub/', N'fu-gocl@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Cờ vây', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/fptgoclub/', [ContactEmail] = N'fu-gocl@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-GOCL';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-CHES')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-CHES', N'CLB Cờ (Chess)', N'SPORTS', N'HAN', N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTUChessClub/', N'fu-ches@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Cờ (Chess)', [Category] = N'SPORTS', [CampusCode] = N'HAN', [Description] = N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/FPTUChessClub/', [ContactEmail] = N'fu-ches@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-CHES';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FCC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FCC', N'FCC - FPT Chess Club', N'SPORTS', N'HCM', N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FptChessClubFcc/', N'hcm-fcc@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FCC - FPT Chess Club', [Category] = N'SPORTS', [CampusCode] = N'HCM', [Description] = N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/FptChessClubFcc/', [ContactEmail] = N'hcm-fcc@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FCC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-FBG')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-FBG', N'FBG - FPT Boardgame Club', N'ACADEMIC', N'HCM', N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fptboardgameclu', N'hcm-fbg@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'FBG - FPT Boardgame Club', [Category] = N'ACADEMIC', [CampusCode] = N'HCM', [Description] = N'Trò chơi & tư duy - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/fptboardgameclu', [ContactEmail] = N'hcm-fbg@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-FBG';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-IGO')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-IGO', N'CLB Tình nguyện vì cộng đồng iGo', N'VOLUNTEER', N'HAN', N'Cộng đồng, tình nguyện & sinh viên - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/iGoClub/', N'fu-igo@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'CLB Tình nguyện vì cộng đồng iGo', [Category] = N'VOLUNTEER', [CampusCode] = N'HAN', [Description] = N'Cộng đồng, tình nguyện & sinh viên - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/iGoClub/', [ContactEmail] = N'fu-igo@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-IGO';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-QVC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-QVC', N'Quỹ Vỏ Chai', N'VOLUNTEER', N'HAN', N'Cộng đồng, tình nguyện & sinh viên - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/qvcfpt/', N'fu-qvc@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Quỹ Vỏ Chai', [Category] = N'VOLUNTEER', [CampusCode] = N'HAN', [Description] = N'Cộng đồng, tình nguyện & sinh viên - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/qvcfpt/', [ContactEmail] = N'fu-qvc@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-QVC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-STUA')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-STUA', N'Hội Sinh viên', N'VOLUNTEER', N'HAN', N'Cộng đồng, tình nguyện & sinh viên - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/hoisinhvien.fu', N'fu-stua@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Hội Sinh viên', [Category] = N'VOLUNTEER', [CampusCode] = N'HAN', [Description] = N'Cộng đồng, tình nguyện & sinh viên - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/hoisinhvien.fu', [ContactEmail] = N'fu-stua@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-STUA';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'HCM-SITI')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'HCM-SITI', N'SitiGroup - Cộng đồng Sinh viên Tình nguyện', N'VOLUNTEER', N'HCM', N'Cộng đồng, tình nguyện & sinh viên - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/sititgroupfuhcm/', N'hcm-siti@fpt.edu.vn', N'02873001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'SitiGroup - Cộng đồng Sinh viên Tình nguyện', [Category] = N'VOLUNTEER', [CampusCode] = N'HCM', [Description] = N'Cộng đồng, tình nguyện & sinh viên - CLB sinh viên Trường Đại học FPT (Hồ Chí Minh). Fanpage: https://www.facebook.com/sititgroupfuhcm/', [ContactEmail] = N'hcm-siti@fpt.edu.vn', [ContactPhone] = N'02873001866', [IsActive] = 1 WHERE [Code] = N'HCM-SITI';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-UNIC')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-UNIC', N'Unicamp', N'OTHER', N'HAN', N'Khác / chưa phân loại rõ - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/unicamp.hl', N'fu-unic@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Unicamp', [Category] = N'OTHER', [CampusCode] = N'HAN', [Description] = N'Khác / chưa phân loại rõ - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/unicamp.hl', [ContactEmail] = N'fu-unic@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-UNIC';
END
IF NOT EXISTS (SELECT 1 FROM [dbo].[Clubs] WHERE [Code] = N'FU-COLR')
BEGIN
    INSERT INTO [dbo].[Clubs] ([Code], [Name], [Category], [CampusCode], [Description], [ContactEmail], [ContactPhone], [ScheduleLabel], [IsRecruiting], [IsActive], [ConcurrencyToken], [CreatedAtUtc])
    VALUES (N'FU-COLR', N'Color Team', N'OTHER', N'HAN', N'Khác / chưa phân loại rõ - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/colorteamvn/', N'fu-colr@fpt.edu.vn', N'02473001866', N'Sinh hoạt định kỳ', 1, 1, NEWID(), SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE [dbo].[Clubs] SET [Name] = N'Color Team', [Category] = N'OTHER', [CampusCode] = N'HAN', [Description] = N'Khác / chưa phân loại rõ - CLB sinh viên Trường Đại học FPT (Hà Nội). Fanpage: https://www.facebook.com/colorteamvn/', [ContactEmail] = N'fu-colr@fpt.edu.vn', [ContactPhone] = N'02473001866', [IsActive] = 1 WHERE [Code] = N'FU-COLR';
END
SELECT count(*) as TotalClubs FROM [dbo].[Clubs];
