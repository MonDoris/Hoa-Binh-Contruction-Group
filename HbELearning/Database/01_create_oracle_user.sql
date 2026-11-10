-- File: Scripts/01_create_oracle_user.sql
-- Chạy bằng: sqlplus / as sysdba   →   @D:\HB E-Learning\HbELearning\Scripts\01_create_oracle_user.sql
-- Mục đích: tạo schema riêng cho ứng dụng thay vì dùng SYSTEM (đúng chuẩn bảo mật).

-- 1. Chuyển vào Pluggable Database (đổi ORCLPDB1 nếu lsnrctl status báo tên khác)
ALTER SESSION SET CONTAINER = ORCLPDB1;

-- 2. Bỏ giới hạn hết hạn mật khẩu (tránh phiền khi phát triển)
ALTER PROFILE DEFAULT LIMIT PASSWORD_LIFE_TIME UNLIMITED;

-- 3. Tạo user/schema cho ứng dụng
CREATE USER HBELEARNING IDENTIFIED BY HbE_Learn2026;

-- 4. Cấp quyền
GRANT CREATE SESSION       TO HBELEARNING;
GRANT CREATE TABLE         TO HBELEARNING;
GRANT CREATE SEQUENCE      TO HBELEARNING;
GRANT CREATE VIEW          TO HBELEARNING;
GRANT CREATE PROCEDURE     TO HBELEARNING;
GRANT CREATE TRIGGER       TO HBELEARNING;
ALTER USER HBELEARNING QUOTA UNLIMITED ON USERS;

-- 5. Kiểm tra
SELECT username, account_status FROM dba_users WHERE username = 'HBELEARNING';

-- Gỡ bỏ toàn bộ (chỉ dùng khi cần làm lại từ đầu):
-- DROP USER HBELEARNING CASCADE;
