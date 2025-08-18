namespace TheDiscDb.Web.Barcode
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    public enum Code128Type
    {
        Auto,
        A,
        B,
        C
    };

    internal class Code128LookupTable
    {
        public static List<Code128Item> LookupTable = new List<Code128Item>();

        static Code128LookupTable()
        {
            LookupTable.Add(new Code128Item(0, " ", " ", "00", "11011001100"));
            LookupTable.Add(new Code128Item(1, "!", "!", "01", "11001101100"));
            LookupTable.Add(new Code128Item(2, "\"", "\"", "02", "11001100110"));
            LookupTable.Add(new Code128Item(3, "#", "#", "03", "10010011000"));
            LookupTable.Add(new Code128Item(4, "$", "$", "04", "10010001100"));
            LookupTable.Add(new Code128Item(5, "%", "%", "05", "10001001100"));
            LookupTable.Add(new Code128Item(6, "&", "&", "06", "10011001000"));
            LookupTable.Add(new Code128Item(7, "'", "'", "07", "10011000100"));
            LookupTable.Add(new Code128Item(8, "(", "(", "08", "10001100100"));
            LookupTable.Add(new Code128Item(9, ")", ")", "09", "11001001000"));
            LookupTable.Add(new Code128Item(10, "*", "*", "10", "11001000100"));
            LookupTable.Add(new Code128Item(11, "+", "+", "11", "11000100100"));
            LookupTable.Add(new Code128Item(12, ",", ",", "12", "10110011100"));
            LookupTable.Add(new Code128Item(13, "-", "-", "13", "10011011100"));
            LookupTable.Add(new Code128Item(14, ".", ".", "14", "10011001110"));
            LookupTable.Add(new Code128Item(15, "/", "/", "15", "10111001100"));
            LookupTable.Add(new Code128Item(16, "0", "0", "16", "10011101100"));
            LookupTable.Add(new Code128Item(17, "1", "1", "17", "10011100110"));
            LookupTable.Add(new Code128Item(18, "2", "2", "18", "11001110010"));
            LookupTable.Add(new Code128Item(19, "3", "3", "19", "11001011100"));
            LookupTable.Add(new Code128Item(20, "4", "4", "20", "11001001110"));
            LookupTable.Add(new Code128Item(21, "5", "5", "21", "11011100100"));
            LookupTable.Add(new Code128Item(22, "6", "6", "22", "11001110100"));
            LookupTable.Add(new Code128Item(23, "7", "7", "23", "11101101110"));
            LookupTable.Add(new Code128Item(24, "8", "8", "24", "11101001100"));
            LookupTable.Add(new Code128Item(25, "9", "9", "25", "11100101100"));
            LookupTable.Add(new Code128Item(26, ":", ":", "26", "11100100110"));
            LookupTable.Add(new Code128Item(27, ";", ";", "27", "11101100100"));
            LookupTable.Add(new Code128Item(28, "<", "<", "28", "11100110100"));
            LookupTable.Add(new Code128Item(29, "=", "=", "29", "11100110010"));
            LookupTable.Add(new Code128Item(30, ">", ">", "30", "11011011000"));
            LookupTable.Add(new Code128Item(31, "?", "?", "31", "11011000110"));
            LookupTable.Add(new Code128Item(32, "@", "@", "32", "11000110110"));
            LookupTable.Add(new Code128Item(33, "A", "A", "33", "10100011000"));
            LookupTable.Add(new Code128Item(34, "B", "B", "34", "10001011000"));
            LookupTable.Add(new Code128Item(35, "C", "C", "35", "10001000110"));
            LookupTable.Add(new Code128Item(36, "D", "D", "36", "10110001000"));
            LookupTable.Add(new Code128Item(37, "E", "E", "37", "10001101000"));
            LookupTable.Add(new Code128Item(38, "F", "F", "38", "10001100010"));
            LookupTable.Add(new Code128Item(39, "G", "G", "39", "11010001000"));
            LookupTable.Add(new Code128Item(40, "H", "H", "40", "11000101000"));
            LookupTable.Add(new Code128Item(41, "I", "I", "41", "11000100010"));
            LookupTable.Add(new Code128Item(42, "J", "J", "42", "10110111000"));
            LookupTable.Add(new Code128Item(43, "K", "K", "43", "10110001110"));
            LookupTable.Add(new Code128Item(44, "L", "L", "44", "10001101110"));
            LookupTable.Add(new Code128Item(45, "M", "M", "45", "10111011000"));
            LookupTable.Add(new Code128Item(46, "N", "N", "46", "10111000110"));
            LookupTable.Add(new Code128Item(47, "O", "O", "47", "10001110110"));
            LookupTable.Add(new Code128Item(48, "P", "P", "48", "11101110110"));
            LookupTable.Add(new Code128Item(49, "Q", "Q", "49", "11010001110"));
            LookupTable.Add(new Code128Item(50, "R", "R", "50", "11000101110"));
            LookupTable.Add(new Code128Item(51, "S", "S", "51", "11011101000"));
            LookupTable.Add(new Code128Item(52, "T", "T", "52", "11011100010"));
            LookupTable.Add(new Code128Item(53, "U", "U", "53", "11011101110"));
            LookupTable.Add(new Code128Item(54, "V", "V", "54", "11101011000"));
            LookupTable.Add(new Code128Item(55, "W", "W", "55", "11101000110"));
            LookupTable.Add(new Code128Item(56, "X", "X", "56", "11100010110"));
            LookupTable.Add(new Code128Item(57, "Y", "Y", "57", "11101101000"));
            LookupTable.Add(new Code128Item(58, "Z", "Z", "58", "11101100010"));
            LookupTable.Add(new Code128Item(59, "[", "[", "59", "11100011010"));
            LookupTable.Add(new Code128Item(60, @"\", @"\", "60", "11101111010"));
            LookupTable.Add(new Code128Item(61, "]", "]", "61", "11001000010"));
            LookupTable.Add(new Code128Item(62, "^", "^", "62", "11110001010"));
            LookupTable.Add(new Code128Item(63, "_", "_", "63", "10100110000"));
            LookupTable.Add(new Code128Item(64, "\0", "`", "64", "10100001100"));
            LookupTable.Add(new Code128Item(65, Convert.ToChar(1).ToString(), "a", "65", "10010110000"));
            LookupTable.Add(new Code128Item(66, Convert.ToChar(2).ToString(), "b", "66", "10010000110"));
            LookupTable.Add(new Code128Item(67, Convert.ToChar(3).ToString(), "c", "67", "10000101100"));
            LookupTable.Add(new Code128Item(68, Convert.ToChar(4).ToString(), "d", "68", "10000100110"));
            LookupTable.Add(new Code128Item(69, Convert.ToChar(5).ToString(), "e", "69", "10110010000"));
            LookupTable.Add(new Code128Item(70, Convert.ToChar(6).ToString(), "f", "70", "10110000100"));
            LookupTable.Add(new Code128Item(71, Convert.ToChar(7).ToString(), "g", "71", "10011010000"));
            LookupTable.Add(new Code128Item(72, Convert.ToChar(8).ToString(), "h", "72", "10011000010"));
            LookupTable.Add(new Code128Item(73, Convert.ToChar(9).ToString(), "i", "73", "10000110100"));
            LookupTable.Add(new Code128Item(74, Convert.ToChar(10).ToString(), "j", "74", "10000110010"));
            LookupTable.Add(new Code128Item(75, Convert.ToChar(11).ToString(), "k", "75", "11000010010"));
            LookupTable.Add(new Code128Item(76, Convert.ToChar(12).ToString(), "l", "76", "11001010000"));
            LookupTable.Add(new Code128Item(77, Convert.ToChar(13).ToString(), "m", "77", "11110111010"));
            LookupTable.Add(new Code128Item(78, Convert.ToChar(14).ToString(), "n", "78", "11000010100"));
            LookupTable.Add(new Code128Item(79, Convert.ToChar(15).ToString(), "o", "79", "10001111010"));
            LookupTable.Add(new Code128Item(80, Convert.ToChar(16).ToString(), "p", "80", "10100111100"));
            LookupTable.Add(new Code128Item(81, Convert.ToChar(17).ToString(), "q", "81", "10010111100"));
            LookupTable.Add(new Code128Item(82, Convert.ToChar(18).ToString(), "r", "82", "10010011110"));
            LookupTable.Add(new Code128Item(83, Convert.ToChar(19).ToString(), "s", "83", "10111100100"));
            LookupTable.Add(new Code128Item(84, Convert.ToChar(20).ToString(), "t", "84", "10011110100"));
            LookupTable.Add(new Code128Item(85, Convert.ToChar(21).ToString(), "u", "85", "10011110010"));
            LookupTable.Add(new Code128Item(86, Convert.ToChar(22).ToString(), "v", "86", "11110100100"));
            LookupTable.Add(new Code128Item(87, Convert.ToChar(23).ToString(), "w", "87", "11110010100"));
            LookupTable.Add(new Code128Item(88, Convert.ToChar(24).ToString(), "x", "88", "11110010010"));
            LookupTable.Add(new Code128Item(89, Convert.ToChar(25).ToString(), "y", "89", "11011011110"));
            LookupTable.Add(new Code128Item(90, Convert.ToChar(26).ToString(), "z", "90", "11011110110"));
            LookupTable.Add(new Code128Item(91, Convert.ToChar(27).ToString(), "{", "91", "11110110110"));
            LookupTable.Add(new Code128Item(92, Convert.ToChar(28).ToString(), "|", "92", "10101111000"));
            LookupTable.Add(new Code128Item(93, Convert.ToChar(29).ToString(), "}", "93", "10100011110"));
            LookupTable.Add(new Code128Item(94, Convert.ToChar(30).ToString(), "~", "94", "10001011110"));
            LookupTable.Add(new Code128Item(95, Convert.ToChar(31).ToString(), Convert.ToChar(127).ToString(), "95", "10111101000"));
            LookupTable.Add(new Code128Item(96, Convert.ToChar(202).ToString() /*FNC3*/, Convert.ToChar(202).ToString() /*FNC3*/, "96", "10111100010"));
            LookupTable.Add(new Code128Item(97, Convert.ToChar(201).ToString() /*FNC2*/, Convert.ToChar(201).ToString() /*FNC2*/, "97", "11110101000"));
            LookupTable.Add(new Code128Item(98, "SHIFT", "SHIFT", "98", "11110100010"));
            LookupTable.Add(new Code128Item(99, "CODE_C", "CODE_C", "99", "10111011110"));
            LookupTable.Add(new Code128Item(100, "CODE_B", Convert.ToChar(203).ToString() /*FNC4*/, "CODE_B", "10111101110"));
            LookupTable.Add(new Code128Item(101, Convert.ToChar(203).ToString() /*FNC4*/, "CODE_A", "CODE_A", "11101011110"));
            LookupTable.Add(new Code128Item(102, Convert.ToChar(200).ToString() /*FNC1*/, Convert.ToChar(200).ToString() /*FNC1*/, Convert.ToChar(200).ToString() /*FNC1*/, "11110101110"));
            LookupTable.Add(new Code128Item(103, "START_A", "START_A", "START_A", "11010000100"));
            LookupTable.Add(new Code128Item(104, "START_B", "START_B", "START_B", "11010010000"));
            LookupTable.Add(new Code128Item(105, "START_C", "START_C", "START_C", "11010011100"));
        }

        public Code128Item StopItem = new Code128Item(uint.MaxValue, "STOP", "STOP", "STOP", "11000111010");

        public Code128Item? TryFindByValue(uint value)
        {
            return LookupTable.FirstOrDefault(i => i.Value == value);
        }

        public Code128Item? TryFind(string s)
        {
            // try to find value in the A column
            var item = LookupTable.FirstOrDefault(c => c.A == s); // Case Sensitive

            // try to find value in the B column
            if (item == null)
            {
                item = LookupTable.FirstOrDefault(c => c.B == s); // Case Sensitive
            }

            // try to find value in the C column
            if (item == null)
            {
                item = LookupTable.FirstOrDefault(c => c.C == s); // Case Sensitive
            }

            return item;
        }
    }

    internal class Code128Item
    {
        public uint Value { get; set; }
        public string Encoding { get; set; }
        public string A { get; set; }
        public string B { get; set; }
        public string C { get; set; }

        public Code128Item(uint value, string a, string b, string c, string encoding)
        {
            Value = value;
            A = a;
            B = b;
            C = c;
            Encoding = encoding;
        }
    }

    public class Code128Encoder : BarcodeEncoder
    {
        private Code128LookupTable CodeLookup = new Code128LookupTable();
        private readonly List<string> formattedData = new List<string>();
        private readonly List<string> encodedData = new List<string>();
        
        public Code128Type Type { get; set; } = Code128Type.Auto;

        private string? CalculateCheckDigit()
        {
            uint checkSum = 0;

            for (uint i = 0; i < formattedData.Count; i++)
            {
                //replace apostrophes with double apostrophes for escape chars
                var s = formattedData[(int) i].Replace("'", "''");
                
                var item = CodeLookup.TryFind(s);
                if (item  == null)
                {
                    throw new Exception($"The code '{s}' was not found in the lookup table");
                }

                var addition = item.Value * ((i == 0) ? 1 : i);
                checkSum += addition;
            }

            var remainder = (checkSum % 103);
            var remainderItem = CodeLookup.TryFindByValue(remainder);

            return remainderItem?.Encoding;
        }

        private void BreakUpDataForEncoding(string data)
        {
            var temp = "";
            var tempRawData = data;

            //breaking the raw data up for code A and code B will mess up the encoding
            if (Type == Code128Type.A || Type == Code128Type.B)
            {
                foreach (var c in data)
                {
                    formattedData.Add(c.ToString());
                }

                return;
            }

            if (Type == Code128Type.C)
            {
                if (!CheckNumericOnly(data))
                {
                    throw new Exception("EC128-6: Only numeric values can be encoded with C128-C.");
                }

                //CODE C: adds a 0 to the front of the Data if the length is not divisible by 2
                if (data.Length % 2 > 0)
                {
                    tempRawData = "0" + data;
                }
            }

            foreach (var c in tempRawData)
            {
                if (char.IsNumber(c))
                {
                    if (temp == "")
                    {
                        temp += c;
                    }
                    else
                    {
                        temp += c;
                        formattedData.Add(temp);
                        temp = "";
                    }
                }
                else
                {
                    if (temp != "")
                    {
                        formattedData.Add(temp);
                        temp = "";
                    }

                    formattedData.Add(c.ToString());
                }
            }

            //if something is still in temp go ahead and push it onto the queue
            if (temp != "")
            {
                formattedData.Add(temp);
                temp = "";
            }
        }

        private void InsertStartandCodeCharacters(string data)
        {
            if (Type != Code128Type.Auto)
            {
                switch (Type)
                {
                    case Code128Type.A:
                        formattedData.Insert(0, "START_A");
                        break;
                    case Code128Type.B:
                        formattedData.Insert(0, "START_B");
                        break;
                    case Code128Type.C:
                        formattedData.Insert(0, "START_C");
                        break;
                    default:
                        throw new Exception("EC128-4: Unknown start type in fixed type encoding.");
                }
            }
            else
            {
                var matchA = new Regex(@"^[\x00-\x5F\xC8-\xCF]*");
                var matchB = new Regex(@"^[\x20-\x7F\xC8-\xCF]*");
                var matchC = new Regex(@"^[0-9]*$");

                if (matchC.Match(data).Length >= 2)
                {
                    formattedData.Insert(0, "START_C");
                    Type = Code128Type.C;
                }
                else if (matchA.Match(data).Length >= matchB.Match(data).Length)
                {
                    formattedData.Insert(0, "START_A");
                    Type = Code128Type.A;
                }
                else
                {
                    formattedData.Insert(0, "START_B");
                    Type = Code128Type.B;
                }
            }
        }

        public override string Encode(string data)
        {
            //insert the start characters
            InsertStartandCodeCharacters(data);

            //break up data for encoding
            BreakUpDataForEncoding(data);

            var encodedData = "";
            foreach (var s in formattedData)
            {
                //handle exception with apostrophes in select statements
                var s1 = s.Replace("'", "''");
                Code128Item? item;

                //select encoding only for type selected
                switch (Type)
                {
                    case Code128Type.A:
                        item = Code128LookupTable.LookupTable.FirstOrDefault(i => i.A == s1);
                        break;
                    case Code128Type.B:
                        item = Code128LookupTable.LookupTable.FirstOrDefault(i => i.B == s1);
                        break;
                    case Code128Type.C:
                        item = Code128LookupTable.LookupTable.FirstOrDefault(i => i.C == s1);
                        break;
                    default:
                        item = null;
                        break;
                }

                if (item == null)
                    throw new Exception("EC128-5: Could not find encoding of a value( " + s1 + " ) in C128 type " + Type);

                encodedData += item.Encoding;
                this.encodedData.Add(item.Encoding);
            }

            //add the check digit
            var checkDigit = CalculateCheckDigit();
            if (checkDigit != null)
            {
                encodedData += checkDigit;
                this.encodedData.Add(checkDigit);
            }

            //add the stop character
            encodedData += CodeLookup.StopItem.Encoding;
            this.encodedData.Add(CodeLookup.StopItem.Encoding);

            //add the termination bars
            encodedData += "11";
            this.encodedData.Add("11");

            return encodedData;
        }
    }
}
