namespace SprocketCarouselAutoloader;

// 0.2.23: one drum seen from the front, chambers in two concentric rings (12 outer, 6 inner,
// like the 75 mm spec), a hub in the middle and the loading trough at the outlet chamber.
// Rows run top-to-bottom like the fixture PNG (tests\Fixtures\revolver-approved.png).
public static class RevolverIcon
{
    public static byte[] Pixels()
    {
        var pixels = new byte[128 * 128 * 4];
        byte[] edge = {122,135,148}, black = {31,36,41}, brass = {212,166,64}, steel = {82,95,106}, bright = {186,203,192}, red = {147,62,52};
        void Rect(int x, int y, int w, int h, byte[] color)
        {
            for (int j = Math.Max(y,0); j < Math.Min(y+h,128); j++)
                for (int i = Math.Max(x,0); i < Math.Min(x+w,128); i++)
                {
                    int n = (j*128+i)*4;
                    pixels[n]=color[0]; pixels[n+1]=color[1]; pixels[n+2]=color[2]; pixels[n+3]=255;
                }
        }
        void Circle(int x, int y, int radius, byte[] color)
        {
            for (int j=-radius;j<=radius;j++) for (int i=-radius;i<=radius;i++)
                if(i*i+j*j<=radius*radius) Rect(x+i,y+j,1,1,color);
        }
        void Chamber(double radius, double angle)
        {
            int cx=(int)Math.Floor(62+radius*Math.Cos(angle)+.5), cy=(int)Math.Floor(64-radius*Math.Sin(angle)+.5);
            Circle(cx,cy,7,black); Circle(cx,cy,4,brass);
        }
        Circle(62,64,58,edge); Circle(62,64,54,steel);
        for (int k=0;k<12;k++) Chamber(39,k*Math.PI/6);
        for (int k=0;k<6;k++) Chamber(20,k*Math.PI/3+Math.PI/6);
        Circle(62,64,8,edge); Circle(62,64,5,black);
        // Loading trough and ram head at the outlet chamber (right, angle 0).
        Rect(104,72,22,6,bright); Rect(118,56,6,22,bright); Circle(101,64,6,red);
        return pixels;
    }
}
