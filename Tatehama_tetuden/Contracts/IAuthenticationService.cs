using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RailwayPhone
{
    /// <summary>
    /// 認証サービスのインターフェース
    /// </summary>
    public interface IAuthenticationService
    {
        /// <summary>
        /// ブラウザベースの OAuth 2.0 認証フローを実行
        /// </summary>
        /// <returns>認証成功時 true、失敗時 false</returns>
        Task<bool> AuthorizeAsync();

        /// <summary>
        /// RefreshToken を使用してアクセストークンを更新
        /// </summary>
        /// <returns>更新成功時 true、失敗時 false</returns>
        Task<bool> RefreshTokenAsync();

        /// <summary>
        /// 現在の有効なアクセストークンを取得
        /// </summary>
        /// <returns>アクセストークン（未認証の場合は null）</returns>
        string? GetAccessToken();

        /// <summary>
        /// 認証後、ユーザーに許可された駅のリストを取得
        /// </summary>
        /// <param name="phoneBookRepo">電話帳リポジトリ</param>
        /// <returns>許可された駅のリスト</returns>
        Task<List<PhoneBookEntry>> GetAllowedStationsAsync(PhoneBookRepository phoneBookRepo);

        /// <summary>
        /// トークンの有効期限
        /// </summary>
        DateTime? TokenExpiration { get; }

        /// <summary>
        /// 認証完了時に発生するイベント
        /// </summary>
        event Action? AuthenticationCompleted;

        /// <summary>
        /// 認証失敗時に発生するイベント
        /// </summary>
        event Action<string>? AuthenticationFailed;
    }
}
