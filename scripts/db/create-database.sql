/*
  ContentPlatform — SUNUCU veritabanı + uygulama kullanıcısı (SQL Server 2022)
  Bu script'i SUNUCUDA bir kez çalıştır. ŞİFREYİ MUTLAKA DEĞİŞTİR: 'REPLACE_ON_SERVER'.
  Not: Şema/tablolar migration ile uygulama ayağa kalkınca otomatik oluşur.
       Bu yüzden kullanıcı db_owner olmalı (migration şema oluşturabilsin).
*/

IF DB_ID(N'ContentPlatform') IS NULL
    CREATE DATABASE [ContentPlatform];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'contentplatform')
    CREATE LOGIN [contentplatform] WITH PASSWORD = N'REPLACE_ON_SERVER', CHECK_POLICY = ON;
GO

USE [ContentPlatform];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'contentplatform')
    CREATE USER [contentplatform] FOR LOGIN [contentplatform];
GO

ALTER ROLE db_owner ADD MEMBER [contentplatform];
GO

PRINT 'ContentPlatform veritabanı ve kullanıcısı hazır. Uygulama ilk açılışta migration''ları uygular.';
