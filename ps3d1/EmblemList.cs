using System;

namespace ps3d1
{
    public class EmblemList
    {
        public static EmblemItem[] Emblems = new EmblemItem[]
        {
            new EmblemItem("HUNGER", 1526),
            new EmblemItem("DAWN OF DESTINY", 1527),
            new EmblemItem("7-7 AD INFINITUM", 1528),
            new EmblemItem("HEART OF THE FOUNDATION", 1529),
            new EmblemItem("EYE OF ETERNITY", 1530),
            new EmblemItem("ANCHOR'S END", 1531),
            new EmblemItem("HEXACON 4", 1532),
            new EmblemItem("EARTHBORN", 1533),
            new EmblemItem("THE INNER CIRCLE", 1534),
            new EmblemItem("ARCHER'S HOPE", 1535),
            new EmblemItem("RESURRECTIONIST", 1536),
            new EmblemItem("OMEN OF THE DEAD", 1537),
            new EmblemItem("OMEN OF THE DEAD II", 1538),
            new EmblemItem("OMEN OF CHAOS", 1539),
            new EmblemItem("OMEN OF CHAOS II", 1540),
            new EmblemItem("OMEN OF THE EXODUS", 1541),
            new EmblemItem("OMEN OF THE DECAYER", 1542),
            new EmblemItem("SIGIL OF THE WAR CULT", 1543),
            new EmblemItem("SIGIL OF THE WAR CULT II", 1544),
            new EmblemItem("SIGIL OF THE ETERNAL NIGHT", 1545),
            new EmblemItem("SIGIL OF THE BURNING DAWN", 1546),
            new EmblemItem("SIGIL OF THE COMING WAR", 1547),
            new EmblemItem("SIGIL OF DEVIANCE", 1548),
            new EmblemItem("SIGIL OF THE IRON LORDS", 1549),
            new EmblemItem("SCAR OF RADEGAST", 1550),
            new EmblemItem("BADGE OF THE MONARCHY", 1551),
            new EmblemItem("RUNE OF THE DISCIPLE", 829),
            new EmblemItem("RUNE OF THE ADEPT", 830),
            new EmblemItem("RUNE OF THE ORACLE", 831),
            new EmblemItem("THE RISING NIGHT", 843),
            new EmblemItem("ASPECT OF BLOOD", 823),
            new EmblemItem("ASPECT OF DUST", 824),
            new EmblemItem("ASPECT OF SHADOW", 825),
            new EmblemItem("OFFICER CREST", 826),
            new EmblemItem("VETERAN CREST", 827),
            new EmblemItem("COMMANDER CREST", 828),
            new EmblemItem("FLOW OF KNOWLEDGE", 4447),
            new EmblemItem("CORSAIR'S BADGE", 3642),
            new EmblemItem("CROTA'S END", 2627),
            new EmblemItem("ERIS MORN", 2626),
            new EmblemItem("BLADE OF CROTA", 2625),
            new EmblemItem("SIGIL OF NIGHT", 2624),
            new EmblemItem("DARK HARVEST", 2623),
            new EmblemItem("SUMMERSONG", 2622),
            new EmblemItem("WOLFSGRIN", 2621),
            new EmblemItem("DRAGOON", 2620),
            new EmblemItem("SENTINEL'S CREST", 3643),
            new EmblemItem("PALADIN'S BLAZON", 3644),
            new EmblemItem("WOLFHUNTER", 3645),
            new EmblemItem("EYE OF OSIRIS", 3641),
            new EmblemItem("SUN OF OSIRIS", 3640),
            new EmblemItem("MOON OF OSIRIS", 3639),
            new EmblemItem("EMPEROR SIGIL", 3638),
            new EmblemItem("PRINCE SIGIL", 3637),
            new EmblemItem("KELLBREAKER", 3636),
            new EmblemItem("JUDGEMENT'S RIGHT HAND", 3635),
            new EmblemItem("FIRM DECREE", 3634),
            new EmblemItem("PRIDE OF NEPAL", 3633),
            new EmblemItem("SUROS FIRE", 4448),
            new EmblemItem("BE BRAVE", 4449),
            new EmblemItem("INSULA THESAURARIA", 4450),
            new EmblemItem("IRON PRIDE", 4451),
            new EmblemItem("THE IRONWOOD TREE", 4452),
            new EmblemItem("HIC JACET", 4453),
            new EmblemItem("SCARAB HEART", 4454),
            new EmblemItem("DEVOURER OF LIGHT", 4455),
            new EmblemItem("KINGSBANE", 4456),
            new EmblemItem("THE ASCENDANT", 4457),
            new EmblemItem("WORM GODS' SERVANT", 4458),
            new EmblemItem("OF LIGHT AND HUNGER", 4459),
            new EmblemItem("NO PUPPET, I", 4460),
            new EmblemItem("OUR KINGDOM", 4461),
            new EmblemItem("VERISIMILITUDE", 4462),
            new EmblemItem("LITTLE LIGHT", 4463),
            new EmblemItem("SILENT SCREAM", 4464),
            new EmblemItem("OFF TO THE RACES", 4465),
            new EmblemItem("CRIMSON CREST", 4466),
            new EmblemItem("SPECTRUM THEORY", 4446),
            new EmblemItem("BERYL COMETARY", 4445),
            new EmblemItem("FRAMES OF MIND", 4444),
            new EmblemItem("SUNSET CITY", 4443),
            new EmblemItem("LOST AND FOUNDRIES", 4442),
            new EmblemItem("EXOMOON", 4441),
            new EmblemItem("LAUREA PRIMA", 4440),
            new EmblemItem("INFINITESIMAL", 4439),
            new EmblemItem("EYE OF THE STORM", 4438),
            new EmblemItem("PAEAN OF RESPLENDENCE", 4437),
            new EmblemItem("THE LIVING WALL", 4436),
            new EmblemItem("THE INEXORABLE", 4435),
            new EmblemItem("MIGHT OF VULCAN", 4434),
            new EmblemItem("ON THE EDGE", 4433),
            new EmblemItem("THE ENTERTAINER", 4432),
            new EmblemItem("THE PERFECT SHOT", 4431)
        };

        public class EmblemItem
        {
            public string Name { get; private set; }
            public ushort Id { get; private set; }

            public EmblemItem(string name, ushort id)
            {
                Name = name;
                Id = id;
            }

            public override string ToString()
            {
                return $"{Name} ({Id})";
            }
        }
    }
}
