using System;
using System.Collections.Generic;
using System.Text;

namespace ft8push
{
    internal class Spot
    {
        public Spot()
        {
            Timestamp = DateTime.Now;
        }

        public DateTime Timestamp { get; set; }
        public string TheirCall { get; set; }
        public string TheirLocator { get; set; }
        public double Distance { get; set; }

        public override string ToString()
        {
            return $"{TheirCall} {TheirLocator} @ {Distance:0}km";
        }
    }
}
