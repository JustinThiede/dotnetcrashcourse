﻿using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Intermediate.Data;
using Intermediate.Dtos;
using Intermediate.Models;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;

namespace Intermediate.Controllers
{
    public class AuthController : ControllerBase
    {
        private readonly DataContextDapper _dataContext;

        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration)
        {
            _dataContext = new DataContextDapper(configuration);
            _configuration = configuration;
        }

        [HttpPost("Register")]
        public IActionResult Register(UserForRegistrationDto userForRegistration)
        {
            if (userForRegistration.Password != userForRegistration.PasswordConfirm) throw new Exception("Passwords do not match!");

            var sqlCheckUserExists = "SELECT Email From TutorialAppSchema.Auth WHERE Email = '" + userForRegistration.Email + "'";
            var existingUsers = _dataContext.LoadData<string>(sqlCheckUserExists);

            if (existingUsers.Any()) throw new Exception("User with this email already exists!");

            var passwordSalt = new byte[16];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetNonZeroBytes(passwordSalt);
            }

            var passwordHash = GetPasswordHash(userForRegistration.Password, passwordSalt);

            var sqlAddAuth = @"INSERT INTO TutorialAppSchema.Auth ([Email], [PasswordHash], [PasswordSalt]) VALUES (@Email, @PasswordHash, @PasswordSalt)";

            var sqlParameters = new List<SqlParameter>();

            var emailParameter = new SqlParameter("@Email", SqlDbType.NVarChar)
            {
                Value = userForRegistration.Email
            };

            var passwordSaltParameter = new SqlParameter("@PasswordSalt", SqlDbType.VarBinary)
            {
                Value = passwordSalt
            };

            var passwordHashParameter = new SqlParameter("@PasswordHash", SqlDbType.VarBinary)
            {
                Value = passwordHash
            };

            sqlParameters.Add(emailParameter);
            sqlParameters.Add(passwordSaltParameter);
            sqlParameters.Add(passwordHashParameter);

            if (_dataContext.ExecuteSqlWithParameters(sqlAddAuth, sqlParameters))
            {
                var sqlAddUser = @"
                            INSERT INTO TutorialAppSchema.Users(
                                [FirstName],
                                [LastName],
                                [Email],
                                [Gender],
                                [Active]
                            ) VALUES (" + "'" + userForRegistration.FirstName + "', '" + userForRegistration.LastName + "', '" + userForRegistration.Email + "', '" + userForRegistration.Gender + "', 1)";


                if (!_dataContext.ExecuteSql(sqlAddUser)) throw new Exception("Failed to add user.");

                return Ok();
            }

            throw new Exception("Failed to register user.");
        }

        [HttpPost("Login")]
        public IActionResult Login(UserForLoginDto userForLogin)
        {
            var sqlForHashAndSalt = @"SELECT 
                [PasswordHash],
                [PasswordSalt] FROM TutorialAppSchema.Auth WHERE Email = '" + userForLogin.Email + "'";

            var userForConfirmation = _dataContext.LoadDataSingle<UserForLoginConfirmationDto>(sqlForHashAndSalt);

            var passwordHash = GetPasswordHash(userForLogin.Password, userForConfirmation.PasswordSalt);

            if (!ConstantTimeComparison(passwordHash, userForConfirmation.PasswordHash)) return StatusCode(401, "Incorrect password!");

            return Ok();
        }

        private byte[] GetPasswordHash(string password, byte[] passwordSalt)
        {
            var passwordSaltPlusString = _configuration.GetSection("AppSettings:PasswordKey").Value + Convert.ToBase64String(passwordSalt);

            return KeyDerivation.Pbkdf2(password, Encoding.ASCII.GetBytes(passwordSaltPlusString), KeyDerivationPrf.HMACSHA256, 100000, 256 / 8);
        }

        private static bool ConstantTimeComparison(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            var result = 0;
            for (var i = 0; i < a.Length; i++) result |= a[i] ^ b[i];

            return result == 0;
        }


        private string CreateToken(int userId)
        {
            Claim[] claims = new Claim[]
            {
                new("userId", userId.ToString())
            };

            var tokenKeyString = _configuration.GetSection("AppSettings:TokenKey").Value;

            var tokenKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKeyString != null ? tokenKeyString : ""));

            var credentials = new SigningCredentials(tokenKey, SecurityAlgorithms.HmacSha512Signature);

            var descriptor = new SecurityTokenDescriptor()
            {
                Subject = new ClaimsIdentity(claims),
                SigningCredentials = credentials,
                Expires = DateTime.Now.AddDays(1)
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            var token = tokenHandler.CreateToken(descriptor);

            return tokenHandler.WriteToken(token);
        }
    }
}
