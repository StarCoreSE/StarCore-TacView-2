public class PIDController
{
    private float kp; // Proportional gain
    private float ki; // Integral gain
    private float kd; // Derivative gain

    private float integral;
    private float previousError;

    public PIDController(float kp, float ki, float kd)
    {
        this.kp = kp;
        this.ki = ki;
        this.kd = kd;
        this.integral = 0f;
        this.previousError = 0f;
    }

    public float Update(float setpoint, float actual, float deltaTime)
    {
        float error = setpoint - actual;
        integral += error * deltaTime;
        float derivative = (error - previousError) / deltaTime;
        previousError = error;

        return kp * error + ki * integral + kd * derivative;
    }
}