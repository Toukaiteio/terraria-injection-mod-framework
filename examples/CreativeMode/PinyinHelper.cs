using System;
using System.Collections.Generic;
using System.Text;

namespace CreativeMode
{
    /// <summary>
    /// Lightweight Chinese pinyin helper for item search.
    /// Provides full pinyin (common chars) + first-letter initials for CJK.
    /// Unknown CJK still matches via the original Chinese name.
    /// </summary>
    internal static class PinyinHelper
    {
        // GB2312 section first-letter boundaries (common lightweight approach).
        private static readonly int[] Boundaries =
        {
            0xB0A1, 0xB0C5, 0xB2C1, 0xB4EE, 0xB6EA, 0xB7A2, 0xB8C1, 0xB9FE,
            0xBBF7, 0xBFA6, 0xC0AC, 0xC2E8, 0xC4C3, 0xC5B6, 0xC5BE, 0xC6DA,
            0xC8BB, 0xC8F6, 0xCBF0, 0xCDDA, 0xCEF4, 0xD1B9, 0xD4D1
        };

        private static readonly char[] BoundaryLetters =
        {
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h',
            'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q',
            'r', 's', 't', 'w', 'x', 'y', 'z'
        };

        private static readonly Dictionary<char, string> Full = BuildFull();
        private static Encoding _gb;

        public static string ToPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var sb = new StringBuilder(text.Length * 3);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c < 0x4E00 || c > 0x9FFF)
                    continue;
                string py;
                if (Full.TryGetValue(c, out py))
                    sb.Append(py);
                else
                    sb.Append(InitialOf(c));
            }
            return sb.ToString().ToLowerInvariant();
        }

        public static string ToInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    string py;
                    if (Full.TryGetValue(c, out py) && py.Length > 0)
                        sb.Append(char.ToLowerInvariant(py[0]));
                    else
                        sb.Append(InitialOf(c));
                }
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        public static bool Matches(string name, string nameLower, string pinyin, string initials, string queryLower)
        {
            if (string.IsNullOrEmpty(queryLower))
                return true;
            if (!string.IsNullOrEmpty(nameLower) && nameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            if (!string.IsNullOrEmpty(name) && name.IndexOf(queryLower, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(pinyin) && pinyin.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            if (!string.IsNullOrEmpty(initials) && initials.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        private static char InitialOf(char c)
        {
            try
            {
                if (_gb == null)
                    _gb = Encoding.GetEncoding("GB2312");
                var bytes = _gb.GetBytes(new[] { c });
                if (bytes.Length < 2)
                    return 'z';
                var code = (bytes[0] << 8) | bytes[1];
                for (var i = Boundaries.Length - 1; i >= 0; i--)
                {
                    if (code >= Boundaries[i])
                        return BoundaryLetters[i];
                }
            }
            catch
            {
                // GB2312 unavailable.
            }
            return 'z';
        }

        private static Dictionary<char, string> BuildFull()
        {
            var d = new Dictionary<char, string>();
            void M(string py, string chars)
            {
                for (var i = 0; i < chars.Length; i++)
                    d[chars[i]] = py;
            }

            // Metals / materials
            M("jin", "金"); M("yin", "银"); M("tong", "铜"); M("tie", "铁");
            M("qian", "铅"); M("xi", "锡"); M("wu", "钨"); M("bo", "铂");
            M("mu", "木"); M("shi", "石"); M("tu", "土"); M("shui", "水");
            M("huo", "火"); M("kuang", "矿"); M("jing", "晶"); M("sha", "沙砂");
            M("ni", "泥"); M("zhuan", "砖"); M("ban", "板"); M("zhu", "柱竹");
            M("liang", "梁"); M("qiang", "墙"); M("men", "门"); M("chuang", "窗床");

            // Weapons / tools
            M("jian", "剑箭键"); M("dao", "刀"); M("qiang", "枪"); M("gong", "弓");
            M("nu", "弩"); M("dan", "弹"); M("fu", "斧"); M("chui", "锤");
            M("gao", "镐"); M("chan", "铲"); M("zuan", "钻"); M("zhang", "杖");
            M("dun", "盾"); M("mao", "矛"); M("ji", "戟"); M("bian", "鞭");

            // Armor
            M("kai", "铠"); M("jia", "甲"); M("kui", "盔"); M("xue", "靴血雪");
            M("shou", "手"); M("tao", "套"); M("jie", "戒"); M("zhi", "指");
            M("chi", "翅"); M("yi", "翼椅"); M("hu", "护"); M("wan", "腕");
            M("xie", "鞋"); M("tui", "腿"); M("fu", "服"); M("zhuang", "装");

            // Items / containers
            M("yao", "药钥妖"); M("wan", "丸碗"); M("ping", "瓶"); M("bao", "宝包堡");
            M("xiang", "箱项像"); M("he", "盒河"); M("dai", "袋"); M("lan", "篮蓝");
            M("sheng", "绳生"); M("suo", "锁"); M("gui", "柜鬼龟"); M("deng", "灯凳");
            M("zhuo", "桌"); M("zao", "灶"); M("lu", "炉"); M("zhong", "钟种");

            // Creatures / bosses words
            M("long", "龙笼"); M("yu", "鱼羽玉雨狱"); M("zhu", "蛛猪"); M("yang", "羊洋");
            M("niu", "牛"); M("ma", "马"); M("niao", "鸟"); M("xia", "虾下");
            M("xie", "蟹械"); M("she", "蛇射"); M("chong", "虫"); M("mo", "魔漠");
            M("gui", "鬼"); M("wang", "王"); M("hou", "后"); M("di", "帝地低");
            M("jiang", "将浆"); M("shi", "士石史世使食饰噬"); M("bing", "兵冰");
            M("qi", "骑旗器"); M("nv", "女"); M("jue", "爵"); M("ling", "灵");
            M("hun", "魂"); M("yan", "眼岩盐"); M("nao", "脑"); M("rou", "肉");
            M("gu", "骨古"); M("jia", "架"); M("shuang", "双"); M("zi", "子紫");
            M("e", "恶"); M("ti", "体"); M("tun", "吞"); M("zhe", "者");
            M("ju", "巨具"); M("shu", "树"); M("yao", "妖"); M("shen", "神深");
            M("tang", "堂"); M("fei", "飞"); M("tian", "天"); M("yue", "月");
            M("liang", "亮"); M("tai", "台太泰"); M("yang", "阳"); M("xing", "星");
            M("chen", "辰"); M("yun", "云"); M("feng", "风"); M("hai", "海");
            M("dao", "岛道"); M("sen", "森"); M("lin", "林"); M("lao", "牢");
            M("cheng", "城橙"); M("miao", "庙苗"); M("tan", "坛"); M("chao", "巢");
            M("xue", "穴"); M("ku", "窟"); M("dong", "洞"); M("quan", "泉");
            M("hu", "湖"); M("xi", "溪锡"); M("bao", "瀑"); M("bu", "布");

            // Colors / adjectives
            M("hong", "红"); M("huang", "黄"); M("lv", "绿"); M("qing", "青");
            M("lan", "蓝"); M("bai", "白百"); M("hei", "黑"); M("hui", "灰");
            M("fen", "粉"); M("zong", "棕"); M("da", "大"); M("xiao", "小");
            M("xin", "新"); M("jiu", "旧九"); M("hao", "好"); M("huai", "坏");
            M("qiang", "强"); M("ruo", "弱"); M("kuai", "快块"); M("man", "慢");
            M("gao", "高"); M("chang", "长"); M("duan", "短"); M("yuan", "远");
            M("jin", "近"); M("qian", "浅千"); M("guang", "光"); M("an", "暗");

            // Numbers
            M("yi", "一"); M("er", "二"); M("san", "三"); M("si", "四");
            M("wu", "五"); M("liu", "六"); M("qi", "七"); M("ba", "八");
            M("shi", "十"); M("wan", "万");

            // Common particles / grammar in item names
            M("zhi", "之枝植"); M("de", "的德"); M("ren", "人"); M("pin", "品");
            M("cai", "材"); M("liao", "料"); M("tiao", "条"); M("pian", "片");
            M("ke", "颗克"); M("di", "滴"); M("ye", "液叶"); M("jiang", "浆");
            M("guan", "罐"); M("tong", "桶"); M("bei", "杯备"); M("pan", "盘");
            M("biao", "表"); M("qi", "器"); M("xie", "械"); M("zhan", "战");
            M("dou", "斗"); M("wu", "武物"); M("zhuang", "装"); M("shi", "饰");
            M("lian", "链"); M("huan", "环"); M("xiang", "项");

            // Fix conflicts for critical chars used above with multi-meaning groups.
            d['金'] = "jin"; d['银'] = "yin"; d['铜'] = "tong"; d['铁'] = "tie";
            d['铅'] = "qian"; d['锡'] = "xi"; d['钨'] = "wu"; d['木'] = "mu";
            d['石'] = "shi"; d['土'] = "tu"; d['水'] = "shui"; d['火'] = "huo";
            d['剑'] = "jian"; d['箭'] = "jian"; d['刀'] = "dao"; d['枪'] = "qiang";
            d['墙'] = "qiang"; d['弓'] = "gong"; d['鱼'] = "yu"; d['龙'] = "long";
            d['血'] = "xue"; d['雪'] = "xue"; d['药'] = "yao"; d['钥'] = "yao";
            d['箱'] = "xiang"; d['王'] = "wang"; d['魔'] = "mo"; d['神'] = "shen";

            return d;
        }
    }
}
