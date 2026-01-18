namespace RailwayPhone
{
    // デバイス情報を管理するクラス
    public class DeviceInfo
    {
        public string Name { get; set; }
        public string ID { get; set; }

        public override string ToString() => Name; // ComboBox表示用
    }
}