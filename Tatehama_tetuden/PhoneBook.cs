using System.Collections.Generic;

namespace RailwayPhone
{
    public class PhoneBookEntry
    {
        public string Name { get; set; }
        public string Number { get; set; }
        public string Category { get; set; } // カテゴリ（色分けなどに使用）
    }

    public static class PhoneBook
    {
        public static List<PhoneBookEntry> Entries { get; } = new List<PhoneBookEntry>
        {
            // --- 100番台: 司令・サーバー ---
            new PhoneBookEntry { Name = "総合司令所 館浜司令", Number = "101", Category = "司令" },
            new PhoneBookEntry { Name = "総合司令所 館浜司令2", Number = "102", Category = "司令" },
            new PhoneBookEntry { Name = "総合司令所 サーバー室", Number = "103", Category = "司令" },
            new PhoneBookEntry { Name = "総合司令所 サーバー室2", Number = "104", Category = "司令" },

            // --- 200番台: 駅信号扱所 ---
            new PhoneBookEntry { Name = "館浜駅 信号扱所", Number = "201", Category = "信号" },
            new PhoneBookEntry { Name = "駒野駅 信号扱所", Number = "202", Category = "信号" },
            new PhoneBookEntry { Name = "津崎駅 信号扱所", Number = "203", Category = "信号" },
            new PhoneBookEntry { Name = "浜園駅 信号扱所", Number = "204", Category = "信号" },
            new PhoneBookEntry { Name = "新野崎駅 信号扱所", Number = "205", Category = "信号" },
            new PhoneBookEntry { Name = "江ノ原検車区 信号扱所", Number = "206", Category = "信号" },
            new PhoneBookEntry { Name = "大道寺駅 信号扱所", Number = "207", Category = "信号" },
            new PhoneBookEntry { Name = "藤江駅 信号扱所", Number = "208", Category = "信号" },
            new PhoneBookEntry { Name = "水越駅 信号扱所", Number = "209", Category = "信号" },
            new PhoneBookEntry { Name = "日野森駅 信号扱所", Number = "210", Category = "信号" },
            new PhoneBookEntry { Name = "赤山町駅 信号扱所", Number = "211", Category = "信号" },

            // --- 300番台: 詰所・駅務室・駅長室 ---
            new PhoneBookEntry { Name = "館浜駅 乗務員詰所", Number = "301", Category = "詰所" },
            new PhoneBookEntry { Name = "新井川 駅務室", Number = "302", Category = "詰所" },
            new PhoneBookEntry { Name = "新野崎 駅長室", Number = "303", Category = "詰所" },
            new PhoneBookEntry { Name = "赤山町駅 乗務員詰所", Number = "304", Category = "詰所" },

            // --- 400番台: 列車区 ---
            new PhoneBookEntry { Name = "駒野列車区", Number = "401", Category = "列車区" },
            new PhoneBookEntry { Name = "大道寺列車区", Number = "402", Category = "列車区" },
        };
    }
}